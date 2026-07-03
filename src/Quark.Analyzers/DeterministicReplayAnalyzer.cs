using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Quark.Analyzers;

/// <summary>
///     Warns when a <c>JournaledGrain&lt;TState,TEvent&gt;.TransitionState</c> override calls a
///     known-nondeterministic API (<c>Guid.NewGuid()</c>, <c>DateTime.Now</c>/<c>UtcNow</c>/
///     <c>Today</c>, <c>DateTimeOffset.Now</c>/<c>UtcNow</c>, or <c>new Random()</c>). This method
///     is documented as "implement as a pure function" because it runs both during original
///     execution and during log replay on reactivation — a nondeterministic call there produces a
///     different value on replay than it did originally, so the replayed state silently diverges
///     from the state that was actually persisted, with no error.
/// </summary>
/// <remarks>
///     This is a narrow, syntactic heuristic (per-project, no cross-assembly call-graph analysis):
///     it only flags calls made directly inside the <c>TransitionState</c> override body. A
///     nondeterministic call hidden behind a helper method invoked from <c>TransitionState</c> is
///     a false negative by design, matching the scope of the other Quark.Analyzers heuristics.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterministicReplayAnalyzer : DiagnosticAnalyzer
{
    private const string JournaledGrainMetadataName = "JournaledGrain`2";
    private const string JournaledGrainNamespace = "Quark.Persistence.Abstractions.Journaling";
    private const string TransitionStateMethodName = "TransitionState";

    public static readonly DiagnosticDescriptor NondeterministicTransitionState = new(
        "QRK0041",
        "TransitionState override calls a nondeterministic API",
        "Method '{0}' overrides JournaledGrain<TState,TEvent>.TransitionState and calls '{1}', a " +
        "nondeterministic API. TransitionState runs both during original execution and during log " +
        "replay on reactivation; since '{1}' returns a different value each time it runs, the " +
        "state produced on replay will silently diverge from the state that was actually " +
        "persisted. Compute the value before raising the event and pass it in as part of the " +
        "event payload instead.",
        "Quark.Determinism",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/wiki/Persistence#event-sourcing");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NondeterministicTransitionState);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation invocation)
            return;

        IMethodSymbol target = invocation.TargetMethod;
        if (target.Name != "NewGuid" || target.ContainingType.ToDisplayString() != "System.Guid")
            return;

        Report(ctx, invocation.Syntax.GetLocation(), "Guid.NewGuid()");
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IObjectCreationOperation creation)
            return;

        // Only the parameterless overload is flagged — it seeds from the current time. A caller
        // passing an explicit seed has opted into (potentially) deterministic behavior, which is
        // outside this heuristic's scope.
        if (creation.Constructor?.ContainingType.ToDisplayString() != "System.Random"
            || creation.Arguments.Length != 0)
            return;

        Report(ctx, creation.Syntax.GetLocation(), "new Random()");
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IPropertyReferenceOperation propertyRef)
            return;

        IPropertySymbol property = propertyRef.Property;
        string containingType = property.ContainingType.ToDisplayString();
        bool isNondeterministicClock =
            (containingType == "System.DateTime" &&
             property.Name is "Now" or "UtcNow" or "Today") ||
            (containingType == "System.DateTimeOffset" &&
             property.Name is "Now" or "UtcNow");
        if (!isNondeterministicClock)
            return;

        Report(ctx, propertyRef.Syntax.GetLocation(), $"{property.ContainingType.Name}.{property.Name}");
    }

    private static void Report(OperationAnalysisContext ctx, Location location, string apiName)
    {
        if (ctx.ContainingSymbol is not IMethodSymbol { Name: TransitionStateMethodName, IsOverride: true } method)
            return;

        if (!OverridesJournaledGrainTransitionState(method))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            NondeterministicTransitionState, location, method.ContainingType.Name, apiName));
    }

    private static bool OverridesJournaledGrainTransitionState(IMethodSymbol method)
    {
        for (IMethodSymbol? m = method; m is not null; m = m.OverriddenMethod)
        {
            INamedTypeSymbol containingType = m.ContainingType.OriginalDefinition;
            if (containingType.MetadataName == JournaledGrainMetadataName &&
                containingType.ContainingNamespace.ToDisplayString() == JournaledGrainNamespace)
                return true;
        }

        return false;
    }
}
