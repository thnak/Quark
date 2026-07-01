using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueTaskPerformanceAnalyzer : DiagnosticAnalyzer
{
    private const string IGrainBehaviorFqn = "Quark.Core.Abstractions.Grains.IGrainBehavior";
    private const string TaskFqn = "System.Threading.Tasks.Task";
    private const string TaskOfTFqn = "System.Threading.Tasks.Task<TResult>";
    private const string ValueTaskFqn = "System.Threading.Tasks.ValueTask";
    private const string ValueTaskOfTFqn = "System.Threading.Tasks.ValueTask<TResult>";

    public static readonly DiagnosticDescriptor TaskReturnOnBehavior = new(
        "QRK0030",
        "Grain behavior method returns Task; consider ValueTask for zero-alloc synchronous paths",
        "Method '{0}' on grain behavior '{1}' returns {2}. ValueTask avoids a heap allocation on synchronous fast paths.",
        "Quark.Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TaskCompletionInValueTaskMethod = new(
        "QRK0031",
        "Use ValueTask-native completion instead of Task.CompletedTask / Task.FromResult in a ValueTask-returning method",
        "'{0}' inside a ValueTask-returning method allocates unnecessarily. Use the ValueTask equivalent instead.",
        "Quark.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(TaskReturnOnBehavior, TaskCompletionInValueTaskMethod);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not IMethodSymbol method)
            return;

        if (method.IsStatic || method.IsAbstract || method.MethodKind != MethodKind.Ordinary)
            return;

        // skip interface members that are explicitly implemented (avoid double-reporting from the concrete class)
        if (method.ExplicitInterfaceImplementations.Length > 0)
            return;

        if (method.ContainingType is not INamedTypeSymbol type || type.TypeKind != TypeKind.Class)
            return;

        bool isGrainBehavior = false;
        foreach (INamedTypeSymbol iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == IGrainBehaviorFqn)
            {
                isGrainBehavior = true;
                break;
            }
        }

        if (!isGrainBehavior)
            return;

        string returnFqn = method.ReturnType.OriginalDefinition.ToDisplayString();
        if (returnFqn != TaskFqn && returnFqn != TaskOfTFqn)
            return;

        Location location = method.Locations.FirstOrDefault() ?? Location.None;
        ctx.ReportDiagnostic(Diagnostic.Create(
            TaskReturnOnBehavior,
            location,
            method.Name,
            type.Name,
            method.ReturnType.ToDisplayString()));
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "CompletedTask")
            return;

        SymbolInfo info = ctx.SemanticModel.GetSymbolInfo(memberAccess, ctx.CancellationToken);
        if (info.Symbol is not IPropertySymbol prop)
            return;

        if (prop.ContainingType.ToDisplayString() != TaskFqn &&
            prop.ContainingType.OriginalDefinition.ToDisplayString() != TaskFqn)
            return;

        if (!IsInsideValueTaskMethod(memberAccess, ctx.SemanticModel, ctx.CancellationToken))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            TaskCompletionInValueTaskMethod,
            memberAccess.GetLocation(),
            "Task.CompletedTask"));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation)
            return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "FromResult")
            return;

        SymbolInfo info = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken);
        if (info.Symbol is not IMethodSymbol method)
            return;

        string containingFqn = method.ContainingType.OriginalDefinition.ToDisplayString();
        if (containingFqn != TaskFqn && containingFqn != TaskOfTFqn)
            return;

        if (!IsInsideValueTaskMethod(invocation, ctx.SemanticModel, ctx.CancellationToken))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            TaskCompletionInValueTaskMethod,
            invocation.GetLocation(),
            "Task.FromResult"));
    }

    private static bool IsInsideValueTaskMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        SyntaxNode? current = node.Parent;
        while (current != null)
        {
            // stop at scope boundaries — don't look past lambdas or local functions
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
                return false;

            if (current is MethodDeclarationSyntax methodDecl)
            {
                IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
                if (methodSymbol == null)
                    return false;

                string returnFqn = methodSymbol.ReturnType.OriginalDefinition.ToDisplayString();
                return returnFqn == ValueTaskFqn || returnFqn == ValueTaskOfTFqn;
            }

            current = current.Parent;
        }

        return false;
    }
}
