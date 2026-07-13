namespace Quark.Runtime;

/// <summary>
///     A single-producer / multi-consumer work-stealing deque, the per-worker local run queue of the
///     next-generation <see cref="ArenaScheduler"/>. The owning worker pushes and pops at the
///     <em>bottom</em> (LIFO — the freshest, cache-warmest item runs next); thieves take from the
///     <em>top</em> (FIFO — the oldest item, fairest to hand off). Lock-free on both owner fast paths;
///     the per-deque <see cref="_foreignLock"/> only serializes the 0-or-1-element boundary between a
///     pop and a steal, plus array growth.
///     <para>
///         This is a faithful port of the algorithm the CLR ThreadPool uses for its
///         <c>WorkStealingQueue&lt;T&gt;</c>: it is a proven design whose correctness rests on the
///         owner reserving a slot by decrementing <see cref="_tailIndex"/> under an
///         <see cref="Interlocked.Exchange(ref int, int)"/> full fence before reading it, and thieves
///         reserving a slot by advancing <see cref="_headIndex"/> under the foreign lock. Only the
///         owner thread may call <see cref="PushBottom"/> / <see cref="TryPopBottom"/>; any thread may
///         call <see cref="TrySteal"/>.
///     </para>
/// </summary>
internal sealed class WorkStealingDeque<T> where T : class
{
    private const int InitialSize = 32;

    private volatile T?[] _array = new T?[InitialSize];
    private volatile int _mask = InitialSize - 1;

    // Owner advances _tailIndex; thieves advance _headIndex (under _foreignLock). Both are read by
    // the opposite side, hence volatile.
    private volatile int _headIndex;
    private volatile int _tailIndex;

    private readonly object _foreignLock = new();

    /// <summary>Approximate element count. May be observed slightly stale by a non-owner; never negative.</summary>
    public int Count
    {
        get
        {
            int count = _tailIndex - _headIndex;
            return count < 0 ? 0 : count;
        }
    }

    public bool IsEmpty => _headIndex >= _tailIndex;

    /// <summary>Owner-only. Pushes an item onto the bottom of the deque.</summary>
    public void PushBottom(T item)
    {
        int tail = _tailIndex;

        // Fast path: there is room without the tail catching the head region.
        if (tail < _headIndex + _mask)
        {
            Volatile.Write(ref _array[tail & _mask], item);
            _tailIndex = tail + 1;
            return;
        }

        // Slow path: (possibly) grow the backing array. Serialize against thieves.
        lock (_foreignLock)
        {
            int head = _headIndex;
            int count = _tailIndex - _headIndex;

            if (count >= _mask)
            {
                // Grow: copy live elements into a fresh, doubled array reindexed from 0.
                var newArray = new T?[_array.Length << 1];
                for (int i = 0; i < _array.Length; i++)
                    newArray[i] = _array[(i + head) & _mask];

                _array = newArray;
                _headIndex = 0;
                _tailIndex = tail = count;
                _mask = (_mask << 1) | 1;
            }

            Volatile.Write(ref _array[tail & _mask], item);
            _tailIndex = tail + 1;
        }
    }

    /// <summary>Owner-only. Pops an item from the bottom of the deque.</summary>
    public bool TryPopBottom(out T? item)
    {
        while (true)
        {
            int tail = _tailIndex;
            if (_headIndex >= tail)
            {
                item = null;
                return false; // empty
            }

            // Reserve the slot by decrementing the tail under a full fence, so a concurrent
            // TrySteal (which reads the tail after advancing the head) observes the reservation.
            tail -= 1;
            Interlocked.Exchange(ref _tailIndex, tail);

            if (_headIndex <= tail)
            {
                // Uncontended: the thief works the head end and cannot reach this slot.
                int idx = tail & _mask;
                item = Volatile.Read(ref _array[idx]);
                if (item is null)
                    continue; // transient null during a grow/steal; re-read

                _array[idx] = null;
                return true;
            }

            // Contended: exactly 0 or 1 elements remain. Resolve against thieves under the lock.
            lock (_foreignLock)
            {
                if (_headIndex <= tail)
                {
                    int idx = tail & _mask;
                    item = _array[idx];
                    _array[idx] = null;
                    if (item is null)
                        continue;
                    return true;
                }

                // A thief took the last element; undo our tail reservation.
                _tailIndex = tail + 1;
                item = null;
                return false;
            }
        }
    }

    /// <summary>Any thread. Steals an item from the top (oldest end) of the deque.</summary>
    public bool TrySteal(out T? item)
    {
        item = null;

        // Lock-free reject when clearly empty; avoids taking the foreign lock on the common miss.
        if (_headIndex >= _tailIndex)
            return false;

        lock (_foreignLock)
        {
            int head = _headIndex;

            // Reserve the head slot under a full fence, so a concurrent TryPopBottom observes it.
            Interlocked.Exchange(ref _headIndex, head + 1);

            if (head < _tailIndex)
            {
                int idx = head & _mask;
                item = Volatile.Read(ref _array[idx]);
                if (item is null)
                {
                    // Cannot happen while head < tail under the lock, but stay safe: undo.
                    _headIndex = head;
                    return false;
                }

                _array[idx] = null;
                return true;
            }

            // Lost the race: the deque emptied under us. Undo the reservation.
            _headIndex = head;
            item = null;
            return false;
        }
    }
}
