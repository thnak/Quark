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
/// </remarks>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly ConcurrentQueue<GrainActivation>[] _shards;

    // Per-shard depth counter, used both as the sweep's "worth trying" pre-check and to gate the
    // idle-wake signal on an exact empty->non-empty transition. ConcurrentQueue<T>.Count cannot be
    // used for this: two concurrent same-shard enqueues can both observe a post-Enqueue Count != 1
    // and both skip Release() -- and because no worker is guaranteed to later go idle and re-sweep
    // (all of them may already be parked), that can strand ready work indefinitely. The
    // Interlocked.Increment(...) == 1 return value is atomic and unique: exactly one concurrent
    // enqueuer observes the transition and releases exactly once. Same pattern proven in the
    // sharded-Channel<T> predecessor of this design
    // (docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md).
    private readonly int[] _shardCounts;

    // Per-shard capacity gate, only allocated when SchedulerReadyQueueCapacity > 0. ConcurrentQueue<T>
    // has no native capacity limit (unlike the Channel<T> it replaces), so bounded-queue backpressure
    // is layered on top via a counting semaphore: ScheduleAsync acquires a slot before enqueueing,
    // RunWorkerAsync releases one after a successful dequeue. Null in the default unbounded case, so
    // the common path (PingPong, AstroSim, most workloads) pays nothing for this.
    private readonly SemaphoreSlim[]? _capacityGates;

    // Idle-wake signal, released only on a shard's exact empty->non-empty transition. Gated by
    // _shardCounts (see that field's comment) via Interlocked.Increment(...) == 1 -- an atomic,
    // race-free transition detector. An earlier version of this gate used a plain
    // ConcurrentQueue<T>.Count == 1 check, which could lose a wakeup under concurrent same-shard
    // enqueues (see docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md section 4,
    // corrected) -- that approach was rejected during review.
    private readonly SemaphoreSlim _workSignal = new(0, int.MaxValue);

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

        _workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            int homeShard = i; // stagger each worker's round-robin start so they don't all sweep in lockstep
            _workers[i] = Task.Run(() => RunWorkerAsync(homeShard, _cts.Token));
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

    /// <summary>Enqueues to the given shard, bumps metrics/diagnostics, and wakes an idle worker on the empty-&gt;non-empty transition.</summary>
    private void EnqueueToShard(int shardIndex, GrainActivation activation)
    {
        ConcurrentQueue<GrainActivation> shard = _shards[shardIndex];
        shard.Enqueue(activation);
        int depth = Interlocked.Increment(ref _shardCounts[shardIndex]);
        if (depth == 1)
            _workSignal.Release();

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
        _workSignal.Dispose();
        if (_capacityGates is not null)
        {
            foreach (SemaphoreSlim gate in _capacityGates)
                gate.Dispose();
        }
    }

    /// <summary>
    ///     Every worker sweeps every shard (starting from its own <paramref name="homeShard"/>, staggered
    ///     so workers don't all scan in lockstep) rather than owning one shard exclusively. This is what
    ///     preserves the "N workers configured means N activations can truly run concurrently" guarantee
    ///     regardless of hash collisions between activations -- sharding only changes which structure a
    ///     given enqueue/dequeue touches, never which worker may service a given activation.
    /// </summary>
    private async Task RunWorkerAsync(int homeShard, CancellationToken ct)
    {
        int shardCount = _shards.Length;
        int cursor = homeShard;

        while (true)
        {
            GrainActivation? activation = null;

            for (int i = 0; i < shardCount; i++)
            {
                if (Volatile.Read(ref _shardCounts[cursor]) > 0 &&
                    _shards[cursor].TryDequeue(out GrainActivation? candidate))
                {
                    Interlocked.Decrement(ref _shardCounts[cursor]);
                    activation = candidate;
                    break;
                }
                cursor = (cursor + 1) % shardCount;
            }

            if (activation is null)
            {
                if (ct.IsCancellationRequested)
                    return;

                // Every shard was empty on a full sweep. Wait on the shared idle signal rather than
                // polling -- don't trust which write woke us, another worker may already have claimed
                // the corresponding item -- just resume the sweep from `cursor`.
                try
                {
                    await _workSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
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
            try
            {
                result = await activation.DrainAsync(_drainBudget, ct).ConfigureAwait(false);
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

            bool needsReschedule = activation.CompleteDrain(result);

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
