using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers;

/// <summary>
/// Code fix provider for converting synchronous actor methods to async.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ActorMethodSignatureCodeFixProvider)), Shared]
public class ActorMethodSignatureCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ActorMethodSignatureAnalyzer.SyncMethodDiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>().FirstOrDefault();

        if (declaration == null)
            return;

        // Register code fix to convert to async Task
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to async Task",
                createChangedDocument: c => ConvertToAsyncTaskAsync(context.Document, declaration, c),
                equivalenceKey: "ConvertToAsyncTask"),
            diagnostic);

        // Register code fix to convert to async ValueTask (if return type is void or simple value type)
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel != null)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
            if (methodSymbol != null && ShouldOfferValueTask(methodSymbol.ReturnType))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Convert to async ValueTask",
                        createChangedDocument: c => ConvertToAsyncValueTaskAsync(context.Document, declaration, c),
                        equivalenceKey: "ConvertToAsyncValueTask"),
                    diagnostic);
            }
        }
    }

    private static bool ShouldOfferValueTask(ITypeSymbol returnType)
    {
        var typeName = returnType.ToDisplayString();
        // Offer ValueTask for void, value types, and simple return types
        return typeName == "void" || returnType.IsValueType || typeName == "string";
    }

    private static async Task<Document> ConvertToAsyncTaskAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        if (methodSymbol == null)
            return document;

        var newReturnType = CreateAsyncReturnType(methodSymbol.ReturnType, useValueTask: false);
        var newMethod = UpdateMethodToAsync(methodDeclaration, newReturnType);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ConvertToAsyncValueTaskAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        if (methodSymbol == null)
            return document;

        var newReturnType = CreateAsyncReturnType(methodSymbol.ReturnType, useValueTask: true);
        var newMethod = UpdateMethodToAsync(methodDeclaration, newReturnType);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static TypeSyntax CreateAsyncReturnType(ITypeSymbol returnType, bool useValueTask)
    {
        var taskType = useValueTask ? "ValueTask" : "Task";
        var typeName = returnType.ToDisplayString();

        if (typeName == "void")
        {
            // void -> Task or ValueTask
            return SyntaxFactory.ParseTypeName(taskType);
        }
        else
        {
            // ReturnType -> Task<ReturnType> or ValueTask<ReturnType>
            return SyntaxFactory.ParseTypeName($"{taskType}<{typeName}>");
        }
    }

    private static MethodDeclarationSyntax UpdateMethodToAsync(
        MethodDeclarationSyntax method,
        TypeSyntax newReturnType)
    {
        var newMethod = method.WithReturnType(newReturnType.WithTrailingTrivia(SyntaxFactory.Space));

        // Add async modifier if not already present
        if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            newMethod = newMethod.WithModifiers(
                method.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
        }

        // Update method body to ensure proper async return
        if (method.Body != null)
        {
            var body = method.Body;
            var hasReturn = body.DescendantNodes().OfType<ReturnStatementSyntax>().Any();

            if (hasReturn)
            {
                // If method has return statements, we need to check if they need Task.FromResult wrapping
                // For simplicity, we'll leave the body as-is and let the developer adjust
                // A more sophisticated implementation could wrap return values
                newMethod = newMethod.WithBody(body);
            }
            else
            {
                // No return statements - likely returns void
                // Ensure the method ends with a Task.CompletedTask or similar
                newMethod = newMethod.WithBody(body);
            }
        }

        return newMethod;
    }
}
