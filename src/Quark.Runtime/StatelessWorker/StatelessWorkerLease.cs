namespace Quark.Runtime.StatelessWorker;

/// <summary>
///     Represents an acquired worker slot for a single stateless-worker call.
///     Dispose frees the slot and releases the pool's execution gate; no-op on a default instance.
/// </summary>
internal readonly struct StatelessWorkerLease : IDisposable
{
    private readonly StatelessWorkerPool? _pool;
    private readonly int _slotIndex;

    public StatelessWorkerLease(GrainId workerId, int slotIndex, StatelessWorkerPool pool)
    {
        WorkerId = workerId;
        _slotIndex = slotIndex;
        _pool = pool;
    }

    /// <summary>The synthetic worker activation identity (<c>W_i</c>) to activate locally.</summary>
    public GrainId WorkerId { get; }

    /// <inheritdoc />
    public void Dispose() => _pool?.FreeSlot(_slotIndex);
}
