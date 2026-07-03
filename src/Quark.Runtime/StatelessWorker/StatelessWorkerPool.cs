namespace Quark.Runtime.StatelessWorker;

/// <summary>
///     Per-logical-GrainId worker pool. Owns a fixed slot array and an execution gate
///     that limits how many workers run concurrently. The pool itself never creates
///     activations; it only selects which synthetic <see cref="GrainId"/> to use.
/// </summary>
internal sealed class StatelessWorkerPool
{
    // Slot state: 0 = free, 1 = busy. Mutated with Interlocked.
    private readonly int[] _slots;
    private readonly SemaphoreSlim _executionGate;
    private readonly StatelessWorkerPoolPolicy _policy;
    private int _waiters;

    public StatelessWorkerPool(StatelessWorkerPoolPolicy policy)
    {
        _policy = policy;
        _slots = new int[policy.MaxLocalActivations];
        _executionGate = new SemaphoreSlim(policy.MaxConcurrentExecutions, policy.MaxConcurrentExecutions);
    }

    /// <summary>
    ///     Acquires a worker slot according to the pool policy.
    ///     Returns the claimed slot index and the synthetic worker id.
    ///     Throws <see cref="SchedulerOverloadException"/> under
    ///     <see cref="SchedulerOverloadMode.RejectWhenFull"/> when the waiter queue is full.
    /// </summary>
    public async ValueTask<(int slotIndex, GrainId workerId)> AcquireAsync(
        GrainId logicalId, CancellationToken ct)
    {
        if (_policy.OverloadMode == SchedulerOverloadMode.RejectWhenFull
            && _policy.QueueCapacity > 0
            && Volatile.Read(ref _waiters) >= _policy.QueueCapacity)
        {
            throw new SchedulerOverloadException(_policy.QueueCapacity);
        }

        Interlocked.Increment(ref _waiters);
        try
        {
            await _executionGate.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
        }

        int slot = ClaimLowestFreeSlot();
        GrainId workerId = StatelessWorkerIdentity.Encode(logicalId, slot);
        return (slot, workerId);
    }

    /// <summary>Releases the slot claimed by a lease (called from <see cref="StatelessWorkerLease.Dispose"/>).</summary>
    public void FreeSlot(int slotIndex)
    {
        Interlocked.Exchange(ref _slots[slotIndex], 0);
        _executionGate.Release();
    }

    /// <summary>
    ///     Called when a worker activation deactivates (idle-collection or explicit).
    ///     Defensively marks the slot free; a no-op if the slot is already free
    ///     (which is the normal case since idle collection only fires on idle workers).
    /// </summary>
    public void MarkWorkerDeactivated(int ordinal)
    {
        Interlocked.CompareExchange(ref _slots[ordinal], 0, 0);
    }

    /// <summary>Returns <c>true</c> when all slots are free (no in-flight calls).</summary>
    public bool IsEmpty
    {
        get
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (Volatile.Read(ref _slots[i]) != 0)
                    return false;
            }
            return true;
        }
    }

    private int ClaimLowestFreeSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (Interlocked.CompareExchange(ref _slots[i], 1, 0) == 0)
                return i;
        }
        // Unreachable: we hold an execution-gate permit, so at least one slot must be free.
        throw new InvalidOperationException(
            "No free slot despite holding an execution permit — pool invariant violated.");
    }
}
