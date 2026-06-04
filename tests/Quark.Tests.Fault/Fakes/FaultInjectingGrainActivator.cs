using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Wraps IGrainActivator to inject faults at grain instantiation time (before OnActivateAsync).
/// Resolves the concrete grain class from the type registry to match ActivationFaultPlan rules.
/// </summary>
public sealed class FaultInjectingGrainActivator : IGrainActivator
{
    private readonly IGrainActivator _inner;
    private readonly IGrainTypeRegistry _registry;
    private readonly ActivationFaultPlan _plan;

    public FaultInjectingGrainActivator(
        IGrainActivator inner,
        IGrainTypeRegistry registry,
        ActivationFaultPlan plan)
    {
        _inner = inner;
        _registry = registry;
        _plan = plan;
    }

    public Grain CreateInstance(GrainId grainId)
    {
        if (_registry.TryGetGrainClass(grainId.Type, out Type? grainClass) && grainClass is not null)
            _plan.Check(grainClass);

        return _inner.CreateInstance(grainId);
    }
}
