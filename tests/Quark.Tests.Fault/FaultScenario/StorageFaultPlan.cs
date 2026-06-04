namespace Quark.Tests.Fault.FaultScenario;

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
