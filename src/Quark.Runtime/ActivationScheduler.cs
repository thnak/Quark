using System.Diagnostics;
using System.Threading.Channels;
using Quark.Diagnostics.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Centralized activation scheduler with a configurable ready queue and drain workers.
///     Options come from <see cref="SiloRuntimeOptions"/>; defaults preserve previous behavior
///     (unbounded ready queue, <see cref="Environment.ProcessorCount"/> workers, no drain budget).
///     Registered as a singleton by <see cref="RuntimeServiceCollectionExtensions.AddQuarkRuntime"/>.
/// </summary>
/// <remarks>
///     The ready queue is sharded (one <see cref="Channel{T}"/> per worker, activation hashed by
///     <see cref="GrainId"/>) purely to spread the write/read synchronization <see cref="Channel{T}"/>
///     requires for its multi-writer/multi-reader case across N independent lock objects instead of
///     one silo-wide channel -- see docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md.
///     Every worker still scans every shard (<see cref="RunWorkerAsync"/>), so a hash collision only
///     costs the contention-reduction benefit for the colliding activations -- it never reduces the
///     <see cref="SiloRuntimeOptions.SchedulerMaxConcurrentActivations"/> concurrency guarantee: any
///     N distinct busy activations can still be serviced by N distinct workers concurrently,
///     regardless of which shard they land on.
/// </remarks>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly Channel<GrainActivation>[] _shards;
    // Approximate per-shard depth, maintained via Interlocked ops alongside (not instead of) each
    // shard's own Channel<T>. Two jobs: (1) let RunWorkerAsync's sweep skip a shard's TryRead --
    // and therefore its internal Channel<T> lock -- entirely when the counter reads 0, so an empty
    // shard costs one Volatile read instead of one Monitor acquisition; (2) let ScheduleAsync only
    // release the idle-wake semaphore on a genuine 0->1 transition instead of on every write (see
    // _workSignal below). Approximate is fine: worst case a stale nonzero count costs one wasted
    // TryRead, never a missed item -- TryRead itself is always the source of truth.
    private readonly int[] _shardCounts;
    // Idle-wake signal: released only on a shard's empty->non-empty transition (see ScheduleAsync),
    // waited on by an idle worker after a full empty sweep. Releasing unconditionally on every
    // write was tried first and was itself a new single-lock bottleneck at ping-pong's message
    // rate (SemaphoreSlim.Release also takes an internal lock); gating on the 0->1 transition cuts
    // release frequency to roughly "a shard just went from empty to non-empty" instead of "a
    // message was posted," which is what actually needs to wake an idle worker.
    // SemaphoreSlim.Release()/WaitAsync() never lose a wakeup regardless of ordering: a Release()
    // that happens before a worker's WaitAsync() call still leaves a banked permit.
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

        _shards = new Channel<GrainActivation>[concurrency];
        _shardCounts = new int[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            _shards[i] = _queueCapacity > 0
                ? Channel.CreateBounded<GrainActivation>(
                    new BoundedChannelOptions(_queueCapacity)
                    {
                        SingleWriter = false,
                        // BoundedChannelFullMode.Wait so WriteAsync blocks when we're in Wait overload mode.
                        // For RejectWhenFull we use TryWrite manually in ScheduleAsync before calling WriteAsync.
                        FullMode = BoundedChannelFullMode.Wait,
                        AllowSynchronousContinuations = false,
                    })
                : Channel.CreateUnbounded<GrainActivation>(
                    new UnboundedChannelOptions { SingleWriter = false, AllowSynchronousContinuations = false });
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
        Channel<GrainActivation> shard = _shards[shardIndex];

        if (_queueCapacity > 0 && _overloadMode == SchedulerOverloadMode.RejectWhenFull)
        {
            if (!shard.Writer.TryWrite(activation))
            {
                activation.AbortSchedule();
                QuarkInstruments.SchedulerOverloadRejections.Add(1);
                _diagnostics.OnSchedulerOverloadRejected(new SchedulerOverloadRejectedEvent(_queueCapacity));
                throw new SchedulerOverloadException(_queueCapacity);
            }
        }
        else
        {
            await shard.Writer.WriteAsync(activation, cancellationToken).ConfigureAwait(false);
        }

        SignalShardWrite(shardIndex);
        QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
        _diagnostics.OnSchedulerReadyQueueDepthChanged(
            new SchedulerReadyQueueDepthChangedEvent(shard.Reader.Count, 1));
    }

    /// <summary>Bumps the approximate per-shard count and wakes an idle worker only on 0-&gt;1.</summary>
    private void SignalShardWrite(int shardIndex)
    {
        if (Interlocked.Increment(ref _shardCounts[shardIndex]) == 1)
            _workSignal.Release();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Channel<GrainActivation> shard in _shards)
            shard.Writer.TryComplete();
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
    }

    /// <summary>
    ///     Every worker sweeps every shard (starting from its own <paramref name="homeShard"/>, staggered
    ///     so workers don't all scan in lockstep) rather than owning one shard exclusively. This is what
    ///     preserves the "N workers configured means N activations can truly run concurrently" guarantee
    ///     regardless of hash collisions between activations -- sharding only changes which lock a given
    ///     write/read contends on, never which worker may service a given activation.
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
                // Skip the shard's own Channel<T> lock entirely when the approximate counter says
                // it's empty -- only attempt TryRead (and thus that shard's lock) on shards that
                // look non-empty. A stale/racy nonzero reading just costs one wasted TryRead, never
                // a missed item.
                if (Volatile.Read(ref _shardCounts[cursor]) > 0
                    && _shards[cursor].Reader.TryRead(out GrainActivation? candidate))
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
                // re-registering a wait on every shard -- don't trust which write woke us, another
                // worker may already have claimed the corresponding item -- just resume the sweep
                // from `cursor`.
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

            QuarkInstruments.SchedulerReadyQueueDepth.Add(-1);
            _diagnostics.OnSchedulerReadyQueueDepthChanged(
                new SchedulerReadyQueueDepthChangedEvent(_shards[cursor].Reader.Count, -1));

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
                    Channel<GrainActivation> rescheduleShard = _shards[rescheduleShardIndex];
                    rescheduleShard.Writer.TryWrite(activation);
                    SignalShardWrite(rescheduleShardIndex);
                    QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
                    _diagnostics.OnSchedulerReadyQueueDepthChanged(
                        new SchedulerReadyQueueDepthChangedEvent(rescheduleShard.Reader.Count, 1));
                }
            }

            cursor = (cursor + 1) % shardCount;
        }
    }
}
