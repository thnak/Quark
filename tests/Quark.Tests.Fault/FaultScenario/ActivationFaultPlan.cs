namespace Quark.Tests.Fault.FaultScenario;

/// <summary>
/// Controls fault injection for grain activation (IBehaviorResolver.Resolve).
/// </summary>
public sealed class ActivationFaultPlan
{
    private readonly List<(Type BehaviorClass, int OnN, Func<Exception> ExFac)> _rules = [];
    private readonly Dictionary<Type, int> _activationCounts = [];

    public ActivationFaultPlan ThrowOnNthActivation<TBehavior>(int n) where TBehavior : class
    {
        _rules.Add((typeof(TBehavior), n,
            () => new InvalidOperationException(
                $"Simulated activation crash for {typeof(TBehavior).Name} (attempt {n})")));
        return this;
    }

    public ActivationFaultPlan ThrowOnNthActivation<TBehavior>(int n, Func<Exception> exFac)
        where TBehavior : class
    {
        _rules.Add((typeof(TBehavior), n, exFac));
        return this;
    }

    internal void Check(Type behaviorClass)
    {
        lock (_activationCounts)
        {
            _activationCounts[behaviorClass] = _activationCounts.GetValueOrDefault(behaviorClass) + 1;
            int count = _activationCounts[behaviorClass];
            foreach (var rule in _rules.Where(r => r.BehaviorClass == behaviorClass && r.OnN == count))
                throw rule.ExFac();
        }
    }
}
