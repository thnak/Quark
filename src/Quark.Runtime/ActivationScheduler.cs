using System.Collections.Concurrent;
using System.Diagnostics;
using Quark.Diagnostics.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Centralized activation scheduler with a configurable ready queue and drain workers.
///     Options come from <see cref="SiloRuntimeOptions"/>; defaults preserve previous behavior
///     (unbounded ready queue, <see cref="Environment.ProcessorCount"/> workers, no drain budget).
///     Registered as a singleton by <see cref="RuntimeServiceCollectionExtensions.AddQuarkRuntime"/>.
/// </summary>
/// <remarks>
///     The ready queue is sharded (one <see cref="ConcurrentQueue{T}"/> per worker, activation hashed
///     by <see cref="GrainId"/>) to spread contention across N independent structures instead of one
///     silo-wide queue -- see docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md
///     and docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md.
///     <see cref="ConcurrentQueue{T}"/> replaces an earlier <c>Channel&lt;GrainActivation&gt;</c>-per-shard
///     design: it's a proven, already-in-the-BCL lock-free MPMC structure with none of Channel's
///     async-waiter/continuation machinery, which profiling showed was being paid even for plain
///     non-blocking dequeue attempts. Every worker still sweeps every shard (see
///     <see cref="RunWorkerAsync"/>), so a hash collision only costs the contention-reduction benefit
///     for the colliding activations -- it never reduces the
///     <see cref="SiloRuntimeOptions.SchedulerMaxConcurrentActivations"/> concurrency guarantee: any
///     N distinct busy activations can still be serviced by N distinct workers concurrently,
///     regardless of which shard they land on.
///     The idle-wake signal is sharded too: a per-worker <see cref="SemaphoreSlim"/> plus a lock-free
///     idle-worker registry (<see cref="_idleWorkers"/>) target a single idle worker directly on each
///     shard's empty-&gt;non-empty transition, instead of every worker contending on one shared
///     semaphore -- see docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md.
///
///     KNOWN HAZARD -- bounded-worker-pool reentrancy deadlock (found 2026-07-10 via the Realm
///     sample's TCP bot-driver benchmark; this is why <c>RuntimeServiceCollectionExtensions</c>
///     currently wires up <see cref="SimpleActivationScheduler"/> instead of this type by default).
///     <see cref="RunWorkerAsync"/> treats a worker as fully busy -- unavailable to dequeue
///     <em>anything</em>, including any other activation's ready-queue entry -- for the entire time
///     it is inside <c>await activation.DrainAndCompleteAsync(...)</c>. A grain method that makes a
///     synchronous cross-activation call (the ordinary shape of any grain-to-grain call: the callee's
///     <c>PostAsync</c> is awaited from inside the caller's own mailbox item) keeps its worker in that
///     busy state for the callee's entire round trip. If the number of activations simultaneously
///     mid-drain-and-blocked-on-such-a-call reaches <see cref="SiloRuntimeOptions.SchedulerMaxConcurrentActivations"/>,
///     and enough of those calls target a shared, not-yet-serviced downstream activation, every
///     worker can end up transitively blocked waiting on a target that only a worker -- and every
///     worker is blocked -- could service. This is a genuine circular self-deadlock, not a rare
///     timing race: reproduced reliably (isolated unit repro, no TCP/DI involved) at worker counts of
///     1, 2, and 4 with a many-callers-fan-in-to-few-targets shape (N caller activations each making
///     a nested <c>PostAsync</c> into one of a handful of "hot" activations, e.g. players calling into
///     a small set of maps); did not reproduce at <see cref="Environment.ProcessorCount"/> workers on
///     a 32-core box purely because 32 simultaneous blocked chains never occurred there -- on a
///     smaller-core deployment, or under enough concurrent callers, the same condition is reachable
///     at the default worker count too, matching the original Realm hang. Confirmed via a captured
///     scheduler-diagnostics trace: the stranded activation's <c>Scheduled</c> event (successful
///     <c>TryMarkScheduled</c> + <c>EnqueueToShard</c>) was followed by a "Waited(6000.8ms)" gap
///     before any worker's <c>DrainStarted</c> -- the ready-queue entry sat untouched, not silently
///     dropped. <see cref="SimpleActivationScheduler"/> is immune by construction: it spawns a fresh,
///     unbounded <c>Task.Run</c> per scheduled activation instead of routing through a fixed pool of
///     worker loops, so a nested call always gets its own execution slot regardless of how many other
///     activations are already mid-drain. A real fix here would need to stop a worker's dispatch loop
///     from being "busy" while blocked on a nested call it isn't actually executing (e.g. spinning up
///     transient extra capacity for reentrant calls, or restructuring drain execution so it never
///     synchronously occupies a worker slot across an inter-activation await) -- out of scope for the
///     investigation that found this; not yet fixed.
/// </remarks>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly ConcurrentQueue<GrainActivation>[] _shards;

    // Per-shard depth counter, used both as the sweep's "worth trying" pre-check and to gate the
    // idle-wake signal on an exact empty->non-empty transition. ConcurrentQueue<T>.Count cannot be
    // used for this: two concurrent same-shard enqueues can both observe a post-Enqueue Count != 1
    // and both skip the wake -- and because no worker is guaranteed to later go idle and re-sweep
    // (all of them may already be parked), that can strand ready work indefinitely. The
    // Interlocked.Increment(...) == 1 return value is atomic and unique: exactly one concurrent
    // enqueuer observes the transition and wakes exactly one worker. Same pattern proven in the
    // sharded-Channel<T> predecessor of this design
    // (docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md).
    private readonly int[] _shardCounts;

    // Per-shard capacity gate, only allocated when SchedulerReadyQueueCapacity > 0. ConcurrentQueue<T>
    // has no native capacity limit (unlike the Channel<T> it replaces), so bounded-queue backpressure
    // is layered on top via a counting semaphore: ScheduleAsync acquires a slot before enqueueing,
    // RunWorkerAsync releases one after a successful dequeue. Null in the default unbounded case, so
    // the common path (PingPong, AstroSim, most workloads) pays nothing for this.
    private readonly SemaphoreSlim[]? _capacityGates;

    // One idle-wake semaphore per worker (indexed by workerIndex, same value as the worker's home
    // shard index -- shard count == worker count, unchanged). Released only for a worker whose index
    // is popped off _idleWorkers, so a given semaphore normally has at most one waiter -- unlike a
    // single shared semaphore, which every worker's WaitAsync/every enqueuer's Release contends on.
    // See docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md section 3.
    private readonly SemaphoreSlim[] _workerSignals;

    // Lock-free registry of worker indices that currently believe they're idle. An enqueuer's exact
    // 0->1 transition (see _shardCounts) pops one entry (if any) and releases that worker's own
    // semaphore -- a targeted wake instead of a broadcast. If the pop finds the stack empty, no
    // release happens; a busy worker will still reach the transitioned shard on its own next full
    // sweep after finishing its current drain, the same fallback the single-shared-semaphore design
    // already relied on (a Release() against an already-positive semaphore doesn't queue a second
    // waiter either).
    //
    // A worker never removes its own entry: ConcurrentStack<T> has no remove-specific-value
    // operation, only Pop (removes whatever is on top), so a "cancel my registration" Pop could
    // remove a DIFFERENT worker's entry instead and strand it -- worse than the contention problem
    // this design exists to fix. Stale entries (a worker listed idle while actually busy, because its
    // own pre-park double-check found work without unregistering) are deliberately accepted: every
    // worker still sweeps every shard unconditionally regardless of signal state, so no work is ever
    // lost from a stale entry -- it only means a future transition's targeted release may land on a
    // busy worker as a wasted credit, which self-corrects by making that worker's next park attempt a
    // non-blocking no-op instead of a real park. See design spec section 4 for the full reasoning.
    private readonly ConcurrentStack<int> _idleWorkers = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;
    private readonly int _drainBudget;
    private readonly int _queueCapacity;
    private readonly SchedulerOverloadMode _overloadMode;
    private readonly IQuarkDiagnosticListener _diagnostics;
    private readonly TimeSpan _shutdownStalledThreshold;

    public ActivationScheduler(
        SiloRuntimeOptions options,
        IQuarkDiagnosticListener? diagnostics = null,
        DiagnosticOptions? diagnosticOptions = null)
    {
        int concurrency = Math.Max(1, options.SchedulerMaxConcurrentActivations);
        _drainBudget = Math.Max(1, options.SchedulerDrainBudget);
        _queueCapacity = options.SchedulerReadyQueueCapacity;
        _overloadMode = options.SchedulerOverloadMode;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
        _shutdownStalledThreshold = (diagnosticOptions ?? new DiagnosticOptions()).ShutdownStalledThreshold;

        _shards = new ConcurrentQueue<GrainActivation>[concurrency];
        for (int i = 0; i < concurrency; i++)
            _shards[i] = new ConcurrentQueue<GrainActivation>();
        _shardCounts = new int[concurrency];

        if (_queueCapacity > 0)
        {
            _capacityGates = new SemaphoreSlim[concurrency];
            for (int i = 0; i < concurrency; i++)
                _capacityGates[i] = new SemaphoreSlim(_queueCapacity, _queueCapacity);
        }

        _workerSignals = new SemaphoreSlim[concurrency];
        for (int i = 0; i < concurrency; i++)
            _workerSignals[i] = new SemaphoreSlim(0, int.MaxValue);

        _workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            int workerIndex = i; // stagger each worker's round-robin start so they don't all sweep in lockstep
            _workers[i] = Task.Run(() => RunWorkerAsync(workerIndex, _cts.Token));
        }
    }

    /// <summary>Deterministic, stable-for-the-activation's-lifetime shard assignment.</summary>
    private int ShardFor(GrainActivation activation)
        => (activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _shards.Length;

    public async ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (!activation.TryMarkScheduled())
            return;

        activation.SetSchedulerEnqueueTime();
        _diagnostics.OnSchedulerActivationScheduled(new SchedulerActivationScheduledEvent(activation.GrainId));

        int shardIndex = ShardFor(activation);

        if (_capacityGates is not null)
        {
            SemaphoreSlim gate = _capacityGates[shardIndex];
            if (_overloadMode == SchedulerOverloadMode.RejectWhenFull)
            {
                if (!gate.Wait(0))
                {
                    activation.AbortSchedule();
                    QuarkInstruments.SchedulerOverloadRejections.Add(1);
                    _diagnostics.OnSchedulerOverloadRejected(new SchedulerOverloadRejectedEvent(_queueCapacity));
                    throw new SchedulerOverloadException(_queueCapacity);
                }
            }
            else
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        EnqueueToShard(shardIndex, activation);
    }

    /// <summary>Enqueues to the given shard, bumps metrics/diagnostics, and wakes one idle worker (if any) on the empty-&gt;non-empty transition.</summary>
    private void EnqueueToShard(int shardIndex, GrainActivation activation)
    {
        ConcurrentQueue<GrainActivation> shard = _shards[shardIndex];
        shard.Enqueue(activation);
        int depth = Interlocked.Increment(ref _shardCounts[shardIndex]);
        if (depth == 1 && _idleWorkers.TryPop(out int idleWorker))
            _workerSignals[idleWorker].Release();

        QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
        _diagnostics.OnSchedulerReadyQueueDepthChanged(
            new SchedulerReadyQueueDepthChangedEvent(depth, 1));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        Task allWorkers = Task.WhenAll(_workers);

        // Surface a stuck shutdown instead of just hanging silently — a hung drain worker
        // otherwise blocks host shutdown indefinitely with no other observable signal. The wait
        // itself is never abandoned; this only reports that it is taking unusually long.
        if (await Task.WhenAny(allWorkers, Task.Delay(_shutdownStalledThreshold)).ConfigureAwait(false) != allWorkers)
        {
            int pending = 0;
            foreach (Task worker in _workers)
            {
                if (!worker.IsCompleted) pending++;
            }

            _diagnostics.OnSchedulerShutdownStalled(
                new SchedulerShutdownStalledEvent(pending, _workers.Length, _shutdownStalledThreshold));
        }

        try
        {
            await allWorkers.ConfigureAwait(false);
        }
        catch
        {
            // Worker cancellation is expected.
        }
        _cts.Dispose();
        foreach (SemaphoreSlim workerSignal in _workerSignals)
            workerSignal.Dispose();
        if (_capacityGates is not null)
        {
            foreach (SemaphoreSlim gate in _capacityGates)
                gate.Dispose();
        }
    }

    /// <summary>
    ///     Sweeps every shard once, starting from and advancing <paramref name="cursor"/>, returning
    ///     the first dequeued activation (if any). On success, <paramref name="cursor"/> is left
    ///     pointing at the shard the activation came from (matching the pre-refactor loop's behavior,
    ///     since later code in <see cref="RunWorkerAsync"/> uses it to release that shard's capacity
    ///     gate and report its depth). Shared by the main dispatch loop and the pre-park double-check
    ///     below so both use identical dequeue logic.
    /// </summary>
    private GrainActivation? TryDequeueAny(ref int cursor)
    {
        int shardCount = _shards.Length;
        for (int i = 0; i < shardCount; i++)
        {
            if (Volatile.Read(ref _shardCounts[cursor]) > 0 &&
                _shards[cursor].TryDequeue(out GrainActivation? candidate))
            {
                Interlocked.Decrement(ref _shardCounts[cursor]);
                return candidate;
            }
            cursor = (cursor + 1) % shardCount;
        }
        return null;
    }

    /// <summary>
    ///     Every worker sweeps every shard (starting from its own <paramref name="workerIndex"/>,
    ///     staggered so workers don't all scan in lockstep) rather than owning one shard exclusively.
    ///     This is what preserves the "N workers configured means N activations can truly run
    ///     concurrently" guarantee regardless of hash collisions between activations -- sharding only
    ///     changes which structure a given enqueue/dequeue touches, never which worker may service a
    ///     given activation.
    /// </summary>
    private async Task RunWorkerAsync(int workerIndex, CancellationToken ct)
    {
        int shardCount = _shards.Length;
        int cursor = workerIndex;

        while (true)
        {
            GrainActivation? activation = TryDequeueAny(ref cursor);

            if (activation is null)
            {
                if (ct.IsCancellationRequested)
                    return;

                // Register as idle, then re-sweep once before parking (double-check). Any
                // transition that landed before this Push is visible to this re-sweep -- the
                // enqueuer's Interlocked.Increment happens-before any TryPop it performs, and this
                // Push happens-before the re-sweep's reads. Any transition landing after this Push
                // is visible to the enqueuer's TryPop, which will target this worker's own
                // semaphore directly, and the subsequent WaitAsync below consumes that credit
                // without blocking. There is no gap between "registered idle" and "unreachable by
                // a wake."
                _idleWorkers.Push(workerIndex);

                activation = TryDequeueAny(ref cursor);

                if (activation is null)
                {
                    // This worker's idle-stack entry is intentionally left in place here -- see
                    // the class-level remarks on _idleWorkers for why a self-removal Pop would be
                    // unsafe. It stays until an enqueuer's TryPop consumes it.
                    try
                    {
                        await _workerSignals[workerIndex].WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    continue;
                }

                // The double-check found work. The idle-stack entry pushed above is deliberately
                // NOT removed -- see the class-level remarks on _idleWorkers. Fall through and
                // process this activation normally.
            }

            _capacityGates?[cursor].Release();
            QuarkInstruments.SchedulerReadyQueueDepth.Add(-1);
            _diagnostics.OnSchedulerReadyQueueDepthChanged(
                new SchedulerReadyQueueDepthChangedEvent(_shardCounts[cursor], -1));

            if (!activation.TryBeginDrain())
            {
                // Should not happen: _scheduled stays claimed for the whole drain (see
                // GrainActivation.CompleteDrain), so this activation cannot have a second ready-queue
                // entry while a drain is in flight. Defensive no-op in case that invariant is ever
                // violated — the in-flight drain's CompleteDrain will reschedule if work remains.
                cursor = (cursor + 1) % shardCount;
                continue;
            }

            long enqueuedAt = activation.TakeSchedulerEnqueueTime();
            double waitMs = enqueuedAt > 0 ? Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds : 0;
            QuarkInstruments.SchedulerActivationWaitDuration.Record(waitMs);
            _diagnostics.OnSchedulerActivationWaited(new SchedulerActivationWaitedEvent(activation.GrainId, waitMs));

            _diagnostics.OnSchedulerDrainStarted(new SchedulerDrainStartedEvent(activation.GrainId));
            QuarkInstruments.SchedulerActiveDrains.Add(1);

            long drainStart = Stopwatch.GetTimestamp();
            ActivationDrainResult result;
            bool needsReschedule;
            try
            {
                (result, needsReschedule) =
                    await activation.DrainAndCompleteAsync(_drainBudget, ct).ConfigureAwait(false);
            }
            finally
            {
                QuarkInstruments.SchedulerActiveDrains.Add(-1);
            }

            double drainMs = Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds;
            QuarkInstruments.SchedulerDrainDuration.Record(drainMs);
            QuarkInstruments.SchedulerDrainItems.Add(result.ItemsProcessed);
            _diagnostics.OnSchedulerDrainCompleted(
                new SchedulerDrainCompletedEvent(activation.GrainId, result.ItemsProcessed, drainMs));

            // Fairness yield: drain hit budget with work still pending.
            if (result.HasMoreWork && result.ItemsProcessed >= _drainBudget)
            {
                QuarkInstruments.SchedulerDrainYields.Add(1);
                _diagnostics.OnSchedulerDrainYielded(
                    new SchedulerDrainYieldedEvent(activation.GrainId, result.ItemsProcessed));
            }

            if (!result.IsCompleted && (result.HasMoreWork || needsReschedule))
            {
                if (activation.TryMarkScheduled())
                {
                    activation.SetSchedulerEnqueueTime();
                    int rescheduleShardIndex = ShardFor(activation);

                    // Matches the pre-existing (pre-work-stealing) design's behavior exactly: a
                    // reschedule that can't fit under a configured SchedulerReadyQueueCapacity is
                    // silently dropped rather than blocking the worker or throwing -- the prior
                    // Channel<T>-based design did the same via a bare TryWrite with an ignored return
                    // value. Not a new behavior introduced by this change; out of scope to fix here.
                    if (_capacityGates is null || _capacityGates[rescheduleShardIndex].Wait(0))
                    {
                        EnqueueToShard(rescheduleShardIndex, activation);
                    }
                }
            }

            cursor = (cursor + 1) % shardCount;
        }
    }
}
