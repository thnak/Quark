using System.Collections.Concurrent;
using System.Diagnostics;
using Quark.Diagnostics.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Next-generation silo scheduler (Phase 1) — a fresh <see cref="IActivationScheduler"/> built on
///     dedicated worker threads, per-worker work-stealing deques, and a shared injection queue. This is
///     the P1 skeleton of the design in
///     docs/superpowers/specs/2026-07-12-next-gen-scheduler-design.md: a single logical arena, no NUMA
///     affinity, one priority lane/band. Later phases layer arenas, affinity, priority lanes, and
///     cooperative cancellation on top of this loop without changing the seam.
/// </summary>
/// <remarks>
///     <para><b>Model.</b> The schedulable unit is the <see cref="GrainActivation"/>, never the message.
///     An activation becomes "ready" when it has undispatched mailbox work; the <c>_scheduled</c> CAS
///     claim (via <see cref="GrainActivation.TryMarkScheduled"/>) guarantees it lives in exactly one
///     queue and is drained by exactly one worker at a time. That single-ownership — not any per-grain
///     lock — is what makes turns thread-safe, and it is what makes stealing safe: a stolen activation
///     carries its exclusive claim.</para>
///
///     <para><b>Routing.</b> A schedule raised <em>from a worker thread</em> (grain-to-grain fan-out)
///     goes onto that worker's local deque — LIFO, cache-warm, stealable. A schedule raised from any
///     other thread (client/network/timer) and every budget-yield reschedule goes onto the shared
///     <see cref="_injection"/> queue (FIFO, fair). Workers drain local-first for locality, poll the
///     injection queue periodically so it can't starve, then steal from siblings.</para>
///
///     <para><b>Parking.</b> Uses the same lost-wakeup-free ordering the legacy scheduler proved out
///     (see docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md): a worker with
///     no work pushes its index onto <see cref="_idleWorkers"/>, re-scans (double-check), then parks on
///     its own semaphore. An enqueuer wakes one idle worker by popping the stack and releasing that
///     worker's semaphore. A stale idle entry left after a double-check finds work is harmless — it is
///     consumed later as a bounded spurious wake.</para>
///
///     <para><b>Async resume (spill-to-ThreadPool on await).</b> A turn that completes
///     <em>synchronously</em> is drained inline on the dedicated worker thread — no async frame, no
///     ThreadPool hop — the CPU-bound throughput fast path where thread/NUMA affinity pays off. The
///     instant a turn <em>awaits</em>, the worker stops waiting on it: the drain's remainder runs as an
///     async continuation (on the ThreadPool, since no per-worker <c>SynchronizationContext</c> is
///     installed and the runtime drains with <c>ConfigureAwait(false)</c>), and the worker returns to
///     its loop free to run other ready activations — including the very nested call the suspended turn
///     is waiting on. This removes both the blocking-drain latency and the bounded-worker reentrancy
///     deadlock (issue #167's failure mode): a non-reentrant call chain deeper than the worker count no
///     longer starves, because no worker is ever blocked inside a turn. The activation's
///     <c>_running</c>/<c>_scheduled</c> claims span the whole async drain (the drain method is
///     suspended, not completed), so single-threaded-per-activation still holds across the await.
///     Await-heavy turns lose thread affinity on resume, which is the right trade — they are I/O-bound,
///     where NUMA locality is irrelevant.</para>
///
///     <para><b>Opt-in.</b> Selected only when <see cref="SiloRuntimeOptions.SchedulerKind"/> is
///     <see cref="SchedulerKind.ArenaV2"/>; the default remains the legacy
///     <see cref="ActivationScheduler"/> until the arena scheduler clears the full benchmark suite.
///     Multi-arena / NUMA affinity and a stall/rescue watchdog for the residual all-workers-suspended
///     edge remain later phases.</para>
/// </remarks>
internal sealed class ArenaScheduler : IActivationScheduler
{
    // Identifies the worker (and its local deque) for the currently-running worker thread, so
    // ScheduleAsync can route a from-worker schedule to the local deque. Null on non-worker threads.
    [ThreadStatic] private static WorkerHandle? _currentWorker;

    private readonly WorkStealingDeque<GrainActivation>[] _deques;

    // One injection sub-queue per worker, external schedules hashed by GrainId across them. Sharding
    // the injection point (rather than one silo-wide queue) spreads producer/consumer contention across
    // N independent MPMC queues — the same contention fix the legacy scheduler's sharded ready queue
    // applies (docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md). Workers
    // service their own shard first and steal from the others, so nothing is stranded on a busy worker.
    private readonly ConcurrentQueue<GrainActivation>[] _inject;

    private readonly SemaphoreSlim[] _signals;
    private readonly Thread[] _threads;
    private readonly ConcurrentStack<int> _idleWorkers = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly IQuarkDiagnosticListener _diagnostics;
    private readonly int _drainBudget;
    private readonly TimeSpan _shutdownStalledThreshold;

    // In-flight backpressure (async-resume). _inFlight[i] = suspended (spilled) drains worker i holds;
    // when it reaches _maxInFlight the worker stops taking new work and parks in ParkForSlot until one
    // of its own drains completes. _capWaiting[i] is the Dekker-fenced flag a completing drain checks to
    // decide whether to wake worker i. Only the spill (async) path touches these, so the synchronous
    // drain fast path pays only a single Volatile.Read of _inFlight[i] (always 0 when nothing spills).
    private readonly int _maxInFlight;
    private readonly int[] _inFlight;
    private readonly int[] _capWaiting;

    // Validation counters — only touched when QUARK_SCHEDULER_STATS=1, so the hot path pays a single
    // predictable-branch check and nothing else when stats are off. Used to prove whether a workload
    // actually exercises from-worker (local-deque) routing and stealing, or is purely externally driven.
    private bool _statsEnabled;
    private long _localSchedules;
    private long _externalSchedules;
    private long _reschedules;
    private long _steals;
    private int _totalInFlight;   // stats-only: current silo-wide suspended drains
    private int _peakInFlight;    // stats-only: high-water mark of _totalInFlight
    private long _capParks;       // stats-only: times a worker hit the in-flight cap and parked

    private volatile bool _disposed;

    /// <summary>Test-only: turns on the validation counters programmatically (equivalent to QUARK_SCHEDULER_STATS=1).</summary>
    internal void EnableStatsForTesting() => _statsEnabled = true;

    /// <summary>Test-only: snapshot of the validation counters (external/local schedules, reschedules, steals).</summary>
    internal (long External, long Local, long Reschedules, long Steals) StatsSnapshot()
        => (Interlocked.Read(ref _externalSchedules),
            Interlocked.Read(ref _localSchedules),
            Interlocked.Read(ref _reschedules),
            Interlocked.Read(ref _steals));

    /// <summary>Test-only: high-water mark of silo-wide concurrent suspended drains (requires stats enabled).</summary>
    internal int PeakInFlightForTesting() => Volatile.Read(ref _peakInFlight);

    public ArenaScheduler(
        SiloRuntimeOptions options,
        IQuarkDiagnosticListener? diagnostics = null,
        DiagnosticOptions? diagnosticOptions = null)
    {
        int workerCount = Math.Max(1, options.SchedulerMaxConcurrentActivations);
        _drainBudget = Math.Max(1, options.SchedulerDrainBudget);
        _maxInFlight = Math.Max(1, options.SchedulerMaxInFlightDrainsPerWorker);
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
        _shutdownStalledThreshold = (diagnosticOptions ?? new DiagnosticOptions()).ShutdownStalledThreshold;
        _statsEnabled = Environment.GetEnvironmentVariable("QUARK_SCHEDULER_STATS") == "1";

        _deques = new WorkStealingDeque<GrainActivation>[workerCount];
        _inject = new ConcurrentQueue<GrainActivation>[workerCount];
        _signals = new SemaphoreSlim[workerCount];
        _threads = new Thread[workerCount];
        _inFlight = new int[workerCount];
        _capWaiting = new int[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            _deques[i] = new WorkStealingDeque<GrainActivation>();
            _inject[i] = new ConcurrentQueue<GrainActivation>();
            _signals[i] = new SemaphoreSlim(0);
        }

        for (int i = 0; i < workerCount; i++)
        {
            int index = i;
            var thread = new Thread(() => WorkerLoop(index))
            {
                IsBackground = true,
                Name = $"quark-arena-worker-{index}",
            };
            _threads[i] = thread;
            thread.Start();
        }
    }

    /// <inheritdoc />
    public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (!activation.TryMarkScheduled())
            return ValueTask.CompletedTask; // already queued/draining — its owner will pick up new work

        _diagnostics.OnSchedulerActivationScheduled(new SchedulerActivationScheduledEvent(activation.GrainId));

        WorkerHandle? current = _currentWorker;
        if (current is not null && current.Owner == this)
        {
            current.Local.PushBottom(activation); // from-worker fan-out → local deque (LIFO, warm)
            if (_statsEnabled) Interlocked.Increment(ref _localSchedules);
        }
        else
        {
            _inject[ShardFor(activation)].Enqueue(activation); // external → hashed injection shard (FIFO, fair)
            if (_statsEnabled) Interlocked.Increment(ref _externalSchedules);
        }

        Wake();
        return ValueTask.CompletedTask;
    }

    /// <summary>Stable-for-the-activation's-lifetime injection shard, so its reschedules stay on one worker.</summary>
    private int ShardFor(GrainActivation activation)
        => (activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _inject.Length;

    /// <summary>
    ///     Re-queues an activation that yielded because it hit the drain budget with work still pending.
    ///     Always routes to the injection queue (back of line) rather than the yielding worker's local
    ///     deque, so a single hot grain cannot monopolize its worker — that is the fairness half of the
    ///     worker budget.
    /// </summary>
    private void Reschedule(GrainActivation activation)
    {
        if (!activation.TryMarkScheduled())
            return;

        _inject[ShardFor(activation)].Enqueue(activation);
        if (_statsEnabled) Interlocked.Increment(ref _reschedules);
        Wake();
    }

    /// <summary>
    ///     Wakes one parked worker, if any is idle. Busy workers discover new work via poll/steal, so
    ///     the common all-busy path must stay off the shared idle stack: the cheap lock-free
    ///     <see cref="ConcurrentStack{T}.IsEmpty"/> read short-circuits before the contended
    ///     <c>TryPop</c> CAS. This only skips a wake when no worker is parked (nothing to wake); a
    ///     worker that parks immediately afterward still catches the work via its post-park double-check,
    ///     so no wake is lost.
    /// </summary>
    private void Wake()
    {
        if (!_idleWorkers.IsEmpty && _idleWorkers.TryPop(out int worker))
            _signals[worker].Release();
    }

    private void WorkerLoop(int index)
    {
        WorkStealingDeque<GrainActivation> local = _deques[index];
        _currentWorker = new WorkerHandle(this, local);
        CancellationToken ct = _cts.Token;
        long iteration = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Backpressure: if we are already holding the max suspended (spilled) drains, take on no
                // more — park until one of ours completes and frees a slot. One Volatile.Read on the hot
                // path; always 0 (never taken) for synchronously-completing workloads.
                if (Volatile.Read(ref _inFlight[index]) >= _maxInFlight)
                {
                    if (!ParkForSlot(index, ct))
                        return;
                    continue;
                }

                GrainActivation? activation = FindWork(index, local, iteration++);
                if (activation is not null)
                {
                    RunActivation(activation, ct, index);
                    continue;
                }

                // No work found. Register idle, then re-scan (double-check) before parking. This
                // push-then-recheck ordering closes the classic lost-wakeup race: any enqueue whose
                // publish happens-before this push is seen by the re-scan; any enqueue after the push
                // sees our idle entry and wakes us.
                _idleWorkers.Push(index);

                activation = FindWork(index, local, iteration++);
                if (activation is not null)
                {
                    // The idle entry we pushed is intentionally left in place — a later enqueuer may
                    // pop it and release our semaphore, producing at most one bounded spurious wake.
                    RunActivation(activation, ct, index);
                    continue;
                }

                try
                {
                    _signals[index].Wait(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            _currentWorker = null;
        }
    }

    /// <summary>Own injection shard + local deque (LIFO) for locality, then steal from siblings.</summary>
    private GrainActivation? FindWork(int index, WorkStealingDeque<GrainActivation> local, long iteration)
    {
        // Every 64th iteration, service the injection shard first so a perpetually hot local deque
        // cannot starve externally-scheduled or yielded work routed to this worker.
        if ((iteration & 63) == 0 && _inject[index].TryDequeue(out GrainActivation? fair))
            return fair;

        if (local.TryPopBottom(out GrainActivation? localItem))
            return localItem;

        if (_inject[index].TryDequeue(out GrainActivation? injected))
            return injected;

        return TryStealFromSiblings(index);
    }

    private GrainActivation? TryStealFromSiblings(int index)
    {
        int n = _deques.Length;
        if (n <= 1)
            return null;

        // Start one past ourselves and sweep, so thieves don't all converge on worker 0. Steal from
        // both a victim's local deque and its injection shard, so work is never stranded on a busy
        // worker's shard (its owner may be blocked in a long drain).
        for (int i = 1; i < n; i++)
        {
            int victim = index + i;
            if (victim >= n)
                victim -= n;

            if (_deques[victim].TrySteal(out GrainActivation? stolen))
            {
                if (_statsEnabled) Interlocked.Increment(ref _steals);
                return stolen;
            }

            if (_inject[victim].TryDequeue(out GrainActivation? injected))
            {
                if (_statsEnabled) Interlocked.Increment(ref _steals);
                return injected;
            }
        }

        return null;
    }

    private void RunActivation(GrainActivation activation, CancellationToken ct, int workerIndex)
    {
        if (!activation.TryBeginDrain())
        {
            // Defensive: the _scheduled claim is held for the whole drain, so a second ready entry for
            // an in-flight drain should never exist. If it somehow does and work remains, requeue it.
            if (activation.HasPendingWork)
                Reschedule(activation);
            return;
        }

        _diagnostics.OnSchedulerDrainStarted(new SchedulerDrainStartedEvent(activation.GrainId));
        long start = Stopwatch.GetTimestamp();

        ValueTask<(ActivationDrainResult Result, bool NeedsReschedule)> drain;
        try
        {
            drain = activation.DrainAndCompleteAsync(_drainBudget, ct);
        }
        catch (OperationCanceledException)
        {
            return; // shutting down before the drain even started
        }

        if (drain.IsCompletedSuccessfully)
        {
            // Synchronously-completing turn: finish inline on this dedicated worker thread — no async
            // frame, no ThreadPool hop. The CPU-bound throughput fast path.
            (ActivationDrainResult result, bool needsReschedule) = drain.Result;
            FinishDrain(activation, result, needsReschedule, start);
        }
        else
        {
            // The turn suspended on an await. Count it against this worker's in-flight budget (before
            // starting the continuation, so a fast completion can never decrement below the increment),
            // then spill the remainder to a ThreadPool continuation so this worker stays free instead of
            // blocking — nested awaited calls then make progress on this same worker (or another), which
            // removes both the blocking-drain latency and the bounded-worker reentrancy deadlock. The
            // in-flight count is what the worker loop's backpressure gate reads. See the class remarks.
            EnterInFlight(workerIndex);
            _ = AwaitDrainAsync(drain, activation, start, workerIndex);
        }
    }

    private async Task AwaitDrainAsync(
        ValueTask<(ActivationDrainResult Result, bool NeedsReschedule)> drain,
        GrainActivation activation,
        long start,
        int workerIndex)
    {
        try
        {
            (ActivationDrainResult result, bool needsReschedule) = await drain.ConfigureAwait(false);
            FinishDrain(activation, result, needsReschedule, start);
        }
        catch (OperationCanceledException)
        {
            // shutting down mid-drain
        }
        finally
        {
            ExitInFlight(workerIndex);
        }
    }

    /// <summary>Records a spilled (suspended) drain against a worker's in-flight budget.</summary>
    private void EnterInFlight(int workerIndex)
    {
        Interlocked.Increment(ref _inFlight[workerIndex]);

        if (_statsEnabled)
        {
            int total = Interlocked.Increment(ref _totalInFlight);
            int peak;
            while (total > (peak = Volatile.Read(ref _peakInFlight)) &&
                   Interlocked.CompareExchange(ref _peakInFlight, total, peak) != peak)
            {
                // retry until the high-water mark reflects this total or a larger one already won
            }
        }
    }

    /// <summary>
    ///     Releases a worker's in-flight slot when a spilled drain completes and wakes the worker if it
    ///     is parked in <see cref="ParkForSlot"/> waiting for a slot. The single-winner CAS on
    ///     <c>_capWaiting</c> ensures exactly one completing drain releases the worker's semaphore per
    ///     park, bounding permit inflation; the Dekker-style full fences (Interlocked here, and the
    ///     Interlocked.Exchange before the re-read in ParkForSlot) guarantee no wake is lost.
    /// </summary>
    private void ExitInFlight(int workerIndex)
    {
        int now = Interlocked.Decrement(ref _inFlight[workerIndex]);
        if (_statsEnabled)
            Interlocked.Decrement(ref _totalInFlight);

        if (now < _maxInFlight &&
            Interlocked.CompareExchange(ref _capWaiting[workerIndex], 0, 1) == 1)
        {
            _signals[workerIndex].Release();
        }
    }

    /// <summary>
    ///     Parks a worker that has hit its in-flight cap until one of its own suspended drains completes
    ///     and frees a slot. Publishes intent (<c>_capWaiting</c>) under a full fence, then re-checks the
    ///     count (double-check) before committing to a wait, so a slot freed between the loop's gate and
    ///     here is not missed. Returns <see langword="false"/> only on shutdown.
    /// </summary>
    private bool ParkForSlot(int index, CancellationToken ct)
    {
        Interlocked.Exchange(ref _capWaiting[index], 1); // publish intent + full fence (Dekker)

        // Double-check: a completion may have freed a slot between the loop's gate read and this publish.
        if (Volatile.Read(ref _inFlight[index]) < _maxInFlight)
        {
            Interlocked.Exchange(ref _capWaiting[index], 0);
            return true;
        }

        if (_statsEnabled) Interlocked.Increment(ref _capParks);

        try
        {
            _signals[index].Wait(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            // Clear intent regardless of how we woke (completion CAS already cleared it, or spurious/cancel).
            Interlocked.Exchange(ref _capWaiting[index], 0);
        }
    }

    /// <summary>Post-drain bookkeeping shared by the inline (sync) and spilled (async) completion paths.</summary>
    private void FinishDrain(GrainActivation activation, ActivationDrainResult result, bool needsReschedule, long start)
    {
        double drainMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _diagnostics.OnSchedulerDrainCompleted(
            new SchedulerDrainCompletedEvent(activation.GrainId, result.ItemsProcessed, drainMs));

        if (!result.IsCompleted && (result.HasMoreWork || needsReschedule))
            Reschedule(activation);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _cts.CancelAsync().ConfigureAwait(false);

        // Wake every worker so parked ones observe cancellation and exit their loops.
        foreach (SemaphoreSlim signal in _signals)
            signal.Release();

        // Join workers off the caller's thread so DisposeAsync stays asynchronous. A worker blocked in
        // a genuinely async drain exits once DrainAsync observes the cancelled token, so joins are
        // prompt in practice; surface a stalled shutdown rather than hanging silently if one does not.
        await Task.Run(() =>
        {
            var stalled = false;
            foreach (Thread thread in _threads)
            {
                if (!thread.Join(_shutdownStalledThreshold))
                    stalled = true;
                else if (stalled)
                    thread.Join(); // already reported; wait out the remainder
            }

            if (stalled)
            {
                int pending = 0;
                foreach (Thread thread in _threads)
                {
                    if (thread.IsAlive)
                        pending++;
                }

                _diagnostics.OnSchedulerShutdownStalled(
                    new SchedulerShutdownStalledEvent(pending, _threads.Length, _shutdownStalledThreshold));

                foreach (Thread thread in _threads)
                    thread.Join();
            }
        }).ConfigureAwait(false);

        _cts.Dispose();
        foreach (SemaphoreSlim signal in _signals)
            signal.Dispose();

        if (_statsEnabled)
        {
            long local = Interlocked.Read(ref _localSchedules);
            long external = Interlocked.Read(ref _externalSchedules);
            long total = local + external;
            double localPct = total == 0 ? 0 : 100.0 * local / total;
            Console.WriteLine(
                $"[ArenaScheduler stats] workers={_threads.Length} maxInFlight/worker={_maxInFlight} " +
                $"schedules: external={external:N0} local(from-worker)={local:N0} ({localPct:F1}%) " +
                $"reschedules={Interlocked.Read(ref _reschedules):N0} steals={Interlocked.Read(ref _steals):N0} " +
                $"peakInFlight={Volatile.Read(ref _peakInFlight):N0} capParks={Interlocked.Read(ref _capParks):N0}");
        }
    }

    /// <summary>Per-worker-thread handle stored in <see cref="_currentWorker"/> for from-worker routing.</summary>
    private sealed class WorkerHandle(ArenaScheduler owner, WorkStealingDeque<GrainActivation> local)
    {
        public ArenaScheduler Owner { get; } = owner;
        public WorkStealingDeque<GrainActivation> Local { get; } = local;
    }
}
