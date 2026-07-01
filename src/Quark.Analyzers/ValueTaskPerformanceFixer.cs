using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class ValueTaskPerformanceFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("QRK0030", "QRK0031");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id == "QRK0030")
                RegisterQrk0030Fix(context, root, diagnostic);
            else if (diagnostic.Id == "QRK0031")
                RegisterQrk0031Fix(context, root, diagnostic);
        }
    }

    private static void RegisterQrk0030Fix(CodeFixContext context, SyntaxNode root, Diagnostic diagnostic)
    {
        SyntaxToken token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        MethodDeclarationSyntax? method = token.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Change return type to ValueTask",
                ct => ApplyQrk0030Async(context.Document, method, ct),
                "QRK0030"),
            diagnostic);
    }

    private static void RegisterQrk0031Fix(CodeFixContext context, SyntaxNode root, Diagnostic diagnostic)
    {
        SyntaxNode? node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ValueTask-native completion",
                ct => ApplyQrk0031Async(context.Document, node, ct),
                "QRK0031"),
            diagnostic);
    }

    // -------------------------------------------------------------------
    // QRK0030 fix
    // -------------------------------------------------------------------

    private static async Task<Document> ApplyQrk0030Async(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken ct)
    {
        SemanticModel? semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        SyntaxNode? root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (semanticModel == null || root == null)
            return document;

        IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method, ct);
        if (methodSymbol == null)
            return document;

        bool isGeneric = methodSymbol.ReturnType.OriginalDefinition.ToDisplayString()
                         == "System.Threading.Tasks.Task<TResult>";
        bool isAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword);

        // Build new return type node
        TypeSyntax newReturnType = BuildValueTaskReturnType(method.ReturnType, isGeneric);

        MethodDeclarationSyntax newMethod = method.WithReturnType(
            newReturnType.WithLeadingTrivia(method.ReturnType.GetLeadingTrivia())
                         .WithTrailingTrivia(method.ReturnType.GetTrailingTrivia()));

        if (!isAsync && method.Body != null)
        {
            var rewriter = new TaskToValueTaskBodyRewriter(semanticModel, isGeneric);
            var newBody = (BlockSyntax)rewriter.Visit(method.Body)!;
            newMethod = newMethod.WithBody(newBody);
        }
        else if (!isAsync && method.ExpressionBody != null)
        {
            var rewriter = new TaskToValueTaskBodyRewriter(semanticModel, isGeneric);
            var newExprBody = (ArrowExpressionClauseSyntax)rewriter.Visit(method.ExpressionBody)!;
            newMethod = newMethod.WithExpressionBody(newExprBody);
        }

        SyntaxNode newRoot = root.ReplaceNode(method, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static TypeSyntax BuildValueTaskReturnType(TypeSyntax originalReturnType, bool isGeneric)
    {
        if (!isGeneric)
            return SyntaxFactory.IdentifierName("ValueTask");

        // originalReturnType is GenericNameSyntax: Task<T> → ValueTask<T>
        if (originalReturnType is GenericNameSyntax generic)
        {
            return SyntaxFactory.GenericName(
                SyntaxFactory.Identifier("ValueTask"),
                generic.TypeArgumentList);
        }

        return SyntaxFactory.IdentifierName("ValueTask");
    }

    // -------------------------------------------------------------------
    // QRK0031 fix
    // -------------------------------------------------------------------

    private static async Task<Document> ApplyQrk0031Async(
        Document document,
        SyntaxNode diagnosticNode,
        CancellationToken ct)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
            return document;

        // The diagnostic node may be the invocation or member access itself.
        // Find the actual node in the root tree.
        SyntaxNode? node = root.FindNode(diagnosticNode.Span, getInnermostNodeForTie: true);
        if (node == null)
            return document;

        SyntaxNode? replacement = null;

        // Task.CompletedTask → ValueTask.CompletedTask
        if (node is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "CompletedTask")
        {
            replacement = memberAccess.WithExpression(
                SyntaxFactory.IdentifierName("ValueTask")
                    .WithLeadingTrivia(memberAccess.Expression.GetLeadingTrivia())
                    .WithTrailingTrivia(memberAccess.Expression.GetTrailingTrivia()));
        }
        // Task.FromResult(x) → ValueTask.FromResult(x)
        else if (node is InvocationExpressionSyntax invocation &&
                 invocation.Expression is MemberAccessExpressionSyntax invMemberAccess &&
                 invMemberAccess.Name.Identifier.Text == "FromResult")
        {
            var newMemberAccess = invMemberAccess.WithExpression(
                SyntaxFactory.IdentifierName("ValueTask")
                    .WithLeadingTrivia(invMemberAccess.Expression.GetLeadingTrivia())
                    .WithTrailingTrivia(invMemberAccess.Expression.GetTrailingTrivia()));
            replacement = invocation.WithExpression(newMemberAccess);
        }

        if (replacement == null)
            return document;

        SyntaxNode newRoot = root.ReplaceNode(node, replacement);
        return document.WithSyntaxRoot(newRoot);
    }

    // -------------------------------------------------------------------
    // Body rewriter — replaces Task.CompletedTask / Task.FromResult / wraps other returns
    // -------------------------------------------------------------------

    private sealed class TaskToValueTaskBodyRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly bool _isGeneric;

        public TaskToValueTaskBodyRewriter(SemanticModel semanticModel, bool isGeneric)
        {
            _semanticModel = semanticModel;
            _isGeneric = isGeneric;
        }

        // Don't descend into nested lambdas/local functions/anonymous methods
        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;
        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;
        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;
        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            // Task.CompletedTask → ValueTask.CompletedTask
            if (node.Name.Identifier.Text == "CompletedTask" &&
                IsTaskSymbol(node))
            {
                return node.WithExpression(
                    SyntaxFactory.IdentifierName("ValueTask")
                        .WithLeadingTrivia(node.Expression.GetLeadingTrivia())
                        .WithTrailingTrivia(node.Expression.GetTrailingTrivia()));
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // Task.FromResult(x) → new ValueTask<T>(x) or ValueTask.FromResult(x)
            if (node.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "FromResult" &&
                IsTaskFromResultSymbol(node))
            {
                if (_isGeneric)
                {
                    // Determine the T from the return type of the Task<T>.FromResult call
                    TypeInfo typeInfo = _semanticModel.GetTypeInfo(node);
                    string typeArg = "object";
                    if (typeInfo.Type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
                        typeArg = named.TypeArguments[0].ToDisplayString();

                    var typeArgSyntax = SyntaxFactory.ParseTypeName(typeArg);
                    var args = node.ArgumentList.Arguments;
                    ExpressionSyntax argExpr = args.Count > 0 ? args[0].Expression : SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

                    return SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.GenericName(
                            SyntaxFactory.Identifier("ValueTask"),
                            SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgSyntax))),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argExpr))),
                        null)
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
                }
                else
                {
                    // Non-generic path: ValueTask.FromResult(x)
                    var newMember = memberAccess.WithExpression(
                        SyntaxFactory.IdentifierName("ValueTask")
                            .WithLeadingTrivia(memberAccess.Expression.GetLeadingTrivia())
                            .WithTrailingTrivia(memberAccess.Expression.GetTrailingTrivia()));
                    return node.WithExpression(newMember);
                }
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression == null)
                return node;

            // Check BEFORE visiting children so we use the original semantic model
            TypeInfo typeInfo = _semanticModel.GetTypeInfo(node.Expression);
            bool returnsTask = IsTaskType(typeInfo.Type);

            // Visit children first (rewrites Task.CompletedTask / Task.FromResult inside)
            var visited = (ReturnStatementSyntax)base.VisitReturnStatement(node)!;

            if (!returnsTask)
                return visited;

            if (visited.Expression == null)
                return visited;

            // If the child rewriter already changed it to a ValueTask expression, don't double-wrap
            TypeInfo newTypeInfo = _semanticModel.GetTypeInfo(node.Expression);
            // We already checked the original; if the expression was rewritten by child visit,
            // the shape may have changed. We use the original type check to decide wrapping.

            ExpressionSyntax expr = visited.Expression;

            // Avoid wrapping something that was already rewritten to a ValueTask literal
            // (Task.CompletedTask → ValueTask.CompletedTask was handled above; its type was still Task in the original model)
            // We need to detect if the child rewriter already replaced the expression.
            // Simple heuristic: if the original expression type was Task, wrap; but if it was already changed
            // to not look like a Task access, still wrap the rewritten form.
            // However ValueTask.CompletedTask and new ValueTask<T>(...) are already the right types.
            // We detect post-rewrite by checking if the expr still references "Task":
            bool alreadyRewritten = IsValueTaskExpression(expr);
            if (alreadyRewritten)
                return visited;

            if (_isGeneric)
            {
                string typeArg = "object";
                if (typeInfo.Type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
                    typeArg = named.TypeArguments[0].ToDisplayString();
                var typeArgSyntax = SyntaxFactory.ParseTypeName(typeArg);

                var wrapped = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier("ValueTask"),
                        SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgSyntax))),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(expr.WithoutTrivia()))),
                    null);

                return visited.WithExpression(wrapped);
            }
            else
            {
                var wrapped = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("ValueTask"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(expr.WithoutTrivia()))),
                    null);

                return visited.WithExpression(wrapped);
            }
        }

        private bool IsTaskSymbol(MemberAccessExpressionSyntax node)
        {
            SymbolInfo info = _semanticModel.GetSymbolInfo(node);
            if (info.Symbol is IPropertySymbol prop)
                return prop.ContainingType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task";
            return false;
        }

        private bool IsTaskFromResultSymbol(InvocationExpressionSyntax node)
        {
            SymbolInfo info = _semanticModel.GetSymbolInfo(node);
            if (info.Symbol is IMethodSymbol method)
                return method.ContainingType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task";
            return false;
        }

        private static bool IsTaskType(ITypeSymbol? type)
        {
            if (type == null)
                return false;
            string fqn = type.OriginalDefinition.ToDisplayString();
            return fqn == "System.Threading.Tasks.Task" || fqn == "System.Threading.Tasks.Task<TResult>";
        }

        private static bool IsValueTaskExpression(ExpressionSyntax expr)
        {
            // Detects: ValueTask.CompletedTask, new ValueTask<T>(...), ValueTask.FromResult(...)
            if (expr is MemberAccessExpressionSyntax ma &&
                ma.Expression is IdentifierNameSyntax id &&
                id.Identifier.Text == "ValueTask")
                return true;

            if (expr is ObjectCreationExpressionSyntax oc)
            {
                if (oc.Type is IdentifierNameSyntax idn && idn.Identifier.Text == "ValueTask")
                    return true;
                if (oc.Type is GenericNameSyntax gn && gn.Identifier.Text == "ValueTask")
                    return true;
            }

            if (expr is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax invMa &&
                invMa.Expression is IdentifierNameSyntax invId &&
                invId.Identifier.Text == "ValueTask")
                return true;

            return false;
        }
    }
}
