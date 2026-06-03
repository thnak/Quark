using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault;

/// <summary>
/// Controls fault injection for IStorage&lt;TState&gt; reads and writes.
/// Thread-safe via Interlocked counters.
/// </summary>
public sealed class StorageFaultPlan
{
    private int _readCount;
    private int _writeCount;

    private readonly List<(bool IsWrite, int OnN, Func<Exception> ExFac)> _throwRules = [];
    private (int OnN, Func<object?> ValueFac)? _staleReadRule;

    public StorageFaultPlan ThrowOnNthWrite<TException>(int n) where TException : Exception, new()
    {
        _throwRules.Add((true, n, () => new TException()));
        return this;
    }

    public StorageFaultPlan ThrowOnNthRead<TException>(int n) where TException : Exception, new()
    {
        _throwRules.Add((false, n, () => new TException()));
        return this;
    }

    /// <summary>
    /// On the Nth read, return <paramref name="staleValue"/> instead of stored state.
    /// Used to simulate a grain reactivating with an incomplete prior run (Status=Processing).
    /// </summary>
    public StorageFaultPlan ReturnStaleOnNthRead<TState>(int n, TState staleValue) where TState : new()
    {
        _staleReadRule = (n, () => staleValue);
        return this;
    }

    internal void CheckWrite()
    {
        int n = Interlocked.Increment(ref _writeCount);
        foreach (var rule in _throwRules.Where(r => r.IsWrite && r.OnN == n))
            throw rule.ExFac();
    }

    internal (bool IsStale, object? Value) CheckRead()
    {
        int n = Interlocked.Increment(ref _readCount);
        foreach (var rule in _throwRules.Where(r => !r.IsWrite && r.OnN == n))
            throw rule.ExFac();
        if (_staleReadRule.HasValue && _staleReadRule.Value.OnN == n)
            return (true, _staleReadRule.Value.ValueFac());
        return (false, null);
    }
}

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
            foreach (var rule in _rules.Where(r => r.Always && r.Key == grainId.Key && r.GrainType == grainId.Type))
                throw rule.ExFac();

            // Increment call count once per call, then check OnNth rules
            _callCountsByType[grainId.Type] = _callCountsByType.GetValueOrDefault(grainId.Type) + 1;
            int count = _callCountsByType[grainId.Type];
            foreach (var rule in _rules.Where(r => !r.Always && r.GrainType == grainId.Type && r.OnN == count))
                throw rule.ExFac();
        }
    }
}

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
