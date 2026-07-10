using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Persistence.InMemory;

/// <summary>
///     In-memory <see cref="ISnapshotStore" /> for development and tests. State is deep-copied on
///     both write and read to isolate the stored snapshot from the grain's live, still-mutating
///     state (the same isolation <see cref="InMemoryGrainStorage" /> applies). Not durable across
///     process restarts, so it never produces the undeserializable-snapshot failure mode.
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<GrainId, (int Version, object State)> _snapshots = new();
    private readonly ICopierProvider _copiers;

    /// <summary>Initializes the in-memory snapshot store.</summary>
    public InMemorySnapshotStore(ICopierProvider copiers) => _copiers = copiers;

    /// <inheritdoc />
    public Task WriteSnapshotAsync<TState>(
        GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        TState isolated = _copiers.GetRequiredCopier<TState>().DeepCopy(snapshot.State, new CopyContext());
        _snapshots[grainId] = (snapshot.Version, isolated);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
        GrainId grainId, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        if (!_snapshots.TryGetValue(grainId, out (int Version, object State) entry))
            return Task.FromResult<SnapshotEnvelope<TState>?>(null);

        TState copy = _copiers.GetRequiredCopier<TState>().DeepCopy((TState)entry.State, new CopyContext());
        return Task.FromResult<SnapshotEnvelope<TState>?>(new SnapshotEnvelope<TState>(entry.Version, copy));
    }

    /// <inheritdoc />
    public Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _snapshots.TryRemove(grainId, out _);
        return Task.CompletedTask;
    }
}
