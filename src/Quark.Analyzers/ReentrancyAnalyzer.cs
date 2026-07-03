using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Quark.Analyzers;

/// <summary>
///     Warns when a non-reentrant grain behavior method <c>await</c>s a call back into one of
///     its own declared grain interfaces — the classic actor-framework self-deadlock: if the
///     call target is this same grain, its mailbox is already occupied processing the current
///     call, so the callback can never be scheduled and the <c>await</c> never completes.
/// </summary>
/// <remarks>
///     This is a narrow, syntactic heuristic (per-project, no cross-assembly call-graph
///     analysis): it flags any inline <c>await someRef.Method()</c> where <c>Method</c> is
///     declared on a grain interface the enclosing behavior itself implements. It cannot tell
///     whether <c>someRef</c> actually resolves to this activation at runtime, so it will also
///     flag safe calls to a different grain of the same interface (a false positive) and will
///     miss self-calls where the task is stored in a local before being awaited (a false
///     negative). Mark the behavior <c>[Reentrant]</c>, or restructure the call, to resolve.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReentrancyAnalyzer : DiagnosticAnalyzer
{
    private const string IGrainBehaviorFqn = "Quark.Core.Abstractions.Grains.IGrainBehavior";
    private const string IGrainFqn = "Quark.Core.Abstractions.Grains.IGrain";
    private const string ReentrantAttributeFqn = "Quark.Core.Abstractions.Grains.ReentrantAttribute";

    public static readonly DiagnosticDescriptor SelfInterfaceAwaitDeadlock = new(
        "QRK0040",
        "Non-reentrant grain behavior awaits a call back into its own grain interface",
        "Method '{0}' on non-reentrant grain behavior '{1}' awaits '{2}.{3}', a method declared " +
        "on the grain interface this behavior implements. If the call target is this same grain, " +
        "its mailbox is already blocked processing this call, so the callback can never be " +
        "scheduled and the await deadlocks. Mark the behavior [Reentrant], route the call through " +
        "a different grain, or restructure the logic to avoid the self-call.",
        "Quark.Reentrancy",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/wiki/Lifecycle-and-Failure-Semantics#mailbox-ordering-and-backpressure");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SelfInterfaceAwaitDeadlock);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation invocation)
            return;

        // Only the direct `await someRef.Method()` shape is detected — a task stored in a
        // local before being awaited bypasses this syntactic heuristic by design (see remarks).
        if (invocation.Parent is not IAwaitOperation)
            return;

        IMethodSymbol targetMethod = invocation.TargetMethod;
        if (targetMethod.ContainingType is not { TypeKind: TypeKind.Interface } targetInterface)
            return;

        if (!ImplementsGrain(targetInterface))
            return;

        if (ctx.ContainingSymbol.ContainingType is not { } behaviorType)
            return;

        if (behaviorType.IsAbstract || behaviorType.TypeKind != TypeKind.Class)
            return;

        if (!IsGrainBehavior(behaviorType))
            return;

        if (IsReentrant(behaviorType))
            return;

        bool implementsTargetInterface = false;
        foreach (INamedTypeSymbol iface in behaviorType.Interfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, targetInterface))
            {
                implementsTargetInterface = true;
                break;
            }
        }

        if (!implementsTargetInterface)
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            SelfInterfaceAwaitDeadlock,
            invocation.Syntax.GetLocation(),
            ctx.ContainingSymbol.Name,
            behaviorType.Name,
            targetInterface.Name,
            targetMethod.Name));
    }

    private static bool ImplementsGrain(INamedTypeSymbol iface)
    {
        if (iface.ToDisplayString() == IGrainFqn)
            return true;

        foreach (INamedTypeSymbol baseIface in iface.AllInterfaces)
        {
            if (baseIface.ToDisplayString() == IGrainFqn)
                return true;
        }

        return false;
    }

    private static bool IsGrainBehavior(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == IGrainBehaviorFqn)
                return true;
        }

        return false;
    }

    // [Reentrant] is Inherited = true, but Roslyn's GetAttributes() only returns attributes
    // declared directly on a symbol, so the base-type chain must be walked explicitly.
    private static bool IsReentrant(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            foreach (AttributeData attribute in t.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ReentrantAttributeFqn)
                    return true;
            }
        }

        return false;
    }
}
