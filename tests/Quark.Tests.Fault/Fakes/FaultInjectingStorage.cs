using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Self-contained IStorage&lt;TState&gt; with configurable fault injection.
/// When no inner storage is provided, maintains its own in-memory store.
/// When inner storage is provided (e.g. real Redis), delegates to it after fault checks.
/// </summary>
public sealed class FaultInjectingStorage<TState> : IStorage<TState> where TState : new()
{
    private readonly Dictionary<string, TState> _store = new();
    private readonly StorageFaultPlan _plan;
    private readonly IStorage<TState>? _inner;  // null = use in-memory dict

    public FaultInjectingStorage(StorageFaultPlan plan, IStorage<TState>? inner = null)
    {
        _plan = plan;
        _inner = inner;
    }

    public async Task<TState> ReadAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        var (isStale, staleValue) = _plan.CheckRead();
        if (isStale)
            return (TState)staleValue!;

        if (_inner is not null)
            return await _inner.ReadAsync(grainId, stateName, ct);

        string key = Key(grainId, stateName);
        return _store.TryGetValue(key, out TState? stored) ? stored : new TState();
    }

    public async Task WriteAsync(GrainId grainId, TState state, string? stateName = null, CancellationToken ct = default)
    {
        _plan.CheckWrite();

        if (_inner is not null)
        {
            await _inner.WriteAsync(grainId, state, stateName, ct);
            return;
        }

        _store[Key(grainId, stateName)] = state;
    }

    public async Task ClearAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        if (_inner is not null)
        {
            await _inner.ClearAsync(grainId, stateName, ct);
            return;
        }
        _store.Remove(Key(grainId, stateName));
    }

    private static string Key(GrainId id, string? name)
        => $"{id.Type.Value}/{id.Key}/{name ?? "Default"}";
}
