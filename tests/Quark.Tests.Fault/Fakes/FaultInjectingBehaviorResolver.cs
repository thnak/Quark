using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Wraps IBehaviorResolver to inject faults at behavior resolution time (before OnActivateAsync).
/// Resolves the concrete behavior class from the type registry to match ActivationFaultPlan rules.
/// </summary>
public sealed class FaultInjectingBehaviorResolver : IBehaviorResolver
{
    private readonly IServiceProvider _sp;
    private readonly IGrainTypeRegistry _registry;
    private readonly ActivationFaultPlan _plan;

    public FaultInjectingBehaviorResolver(
        IServiceProvider sp,
        IGrainTypeRegistry registry,
        ActivationFaultPlan plan)
    {
        _sp = sp;
        _registry = registry;
        _plan = plan;
    }

    public IGrainBehavior Resolve(GrainType grainType)
    {
        if (!_registry.TryGetGrainClass(grainType, out Type? behaviorType) || behaviorType is null)
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");

        _plan.Check(behaviorType);

        return (IGrainBehavior)ActivatorUtilities.CreateInstance(_sp, behaviorType);
    }
}
