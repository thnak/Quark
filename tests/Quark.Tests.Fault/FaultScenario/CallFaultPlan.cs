using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Fault.FaultScenario;

/// <summary>
/// Controls fault injection for inter-grain calls routed through IGrainCallInvoker.
/// Supports targeting by grain type or by grain key.
/// </summary>
public sealed class CallFaultPlan
{
    private readonly List<(GrainType? GrainType, string? Key, int OnN, bool Always, Func<Exception> ExFac)> _rules = [];
    private readonly Dictionary<GrainType, int> _callCountsByType = [];

    /// <summary>Throw on the Nth call to any grain of <paramref name="grainType"/>.</summary>
    public CallFaultPlan ThrowOnNthCallToType(GrainType grainType, int n, Func<Exception> exFac)
    {
        _rules.Add((grainType, null, n, false, exFac));
        return this;
    }

    /// <summary>Always throw for calls to the grain with the specific key (all 3 retry attempts).</summary>
    public CallFaultPlan AlwaysThrowForKey(GrainType grainType, string key, Func<Exception> exFac)
    {
        _rules.Add((grainType, key, 0, true, exFac));
        return this;
    }

    internal void Check(GrainId grainId, uint methodId)
    {
        lock (_callCountsByType)
        {
            // Check Always rules first (by key)
            foreach ((GrainType? GrainType, string? Key, int OnN, bool Always, Func<Exception> ExFac) rule in _rules.Where(r => r.Always && r.Key == grainId.Key && r.GrainType == grainId.Type))
            {
                throw rule.ExFac();
            }

            // Increment call count once per call, then check OnNth rules
            _callCountsByType[grainId.Type] = _callCountsByType.GetValueOrDefault(grainId.Type) + 1;
            int count = _callCountsByType[grainId.Type];
            foreach ((GrainType? GrainType, string? Key, int OnN, bool Always, Func<Exception> ExFac) rule in _rules.Where(r => !r.Always && r.GrainType == grainId.Type && r.OnN == count))
            {
                throw rule.ExFac();
            }
        }
    }
}
