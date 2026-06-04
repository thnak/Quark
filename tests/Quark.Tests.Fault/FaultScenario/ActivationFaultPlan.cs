using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Fault.FaultScenario;

/// <summary>
/// Controls fault injection for grain activation (IGrainActivator.CreateInstance).
/// </summary>
public sealed class ActivationFaultPlan
{
    private readonly List<(Type GrainClass, int OnN, Func<Exception> ExFac)> _rules = [];
    private readonly Dictionary<Type, int> _activationCounts = [];

    public ActivationFaultPlan ThrowOnNthActivation<TGrain>(int n) where TGrain : Grain
    {
        _rules.Add((typeof(TGrain), n, () => new InvalidOperationException($"Simulated activation crash for {typeof(TGrain).Name} (attempt {n})")));
        return this;
    }

    public ActivationFaultPlan ThrowOnNthActivation<TGrain>(int n, Func<Exception> exFac) where TGrain : Grain
    {
        _rules.Add((typeof(TGrain), n, exFac));
        return this;
    }

    internal void Check(Type grainClass)
    {
        lock (_activationCounts)
        {
            _activationCounts[grainClass] = _activationCounts.GetValueOrDefault(grainClass) + 1;
            int count = _activationCounts[grainClass];
            foreach (var rule in _rules.Where(r => r.GrainClass == grainClass && r.OnN == count))
                throw rule.ExFac();
        }
    }
}