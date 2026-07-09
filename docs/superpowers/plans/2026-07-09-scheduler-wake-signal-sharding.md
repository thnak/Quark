# Scheduler Wake-Signal Sharding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ActivationScheduler`'s single shared `SemaphoreSlim _workSignal` with a per-worker `SemaphoreSlim[] _workerSignals` plus a lock-free `ConcurrentStack<int> _idleWorkers` registry, so an enqueue's exact empty→non-empty transition targets exactly one idle worker's semaphore instead of releasing one object every worker contends on.

**Architecture:** Same shape as the currently-shipped `ConcurrentQueue`-per-shard scheduler (commit `7aa7ebc`): N worker shards, activations hash-placed by `GrainId`, every worker sweeps every shard. Only the idle-wake mechanism changes: an enqueuer's `Interlocked.Increment(...) == 1` transition gate now pops one idle worker's index off a `ConcurrentStack<int>` and releases only that worker's own `SemaphoreSlim`, instead of releasing one shared semaphore. A worker parks by pushing its own index onto that stack, re-sweeping once (double-check), then waiting only if the re-sweep also found nothing.

**Tech Stack:** .NET 10, `System.Collections.Concurrent.ConcurrentStack<T>`, `System.Threading.SemaphoreSlim`, xUnit.

## Global Constraints

- Do not change `_shards`, `_shardCounts`, `ShardFor`, `_capacityGates`, or any bounded-queue/backpressure behavior — orthogonal to this change per the spec's non-goals (`docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md` section 7).
- Do not change `GrainActivation`'s mailbox channel, `TryBeginDrain`/`CompleteDrain`, drain-budget/fairness-yield logic, or diagnostics event shapes — orthogonal, untouched.
- Do not attempt to remove a worker's own idle-stack entry when its post-park double-check finds work. `ConcurrentStack<T>` has no remove-specific-value operation, only `Pop` (removes whatever is currently on top) — a "cancel my own registration" `Pop` could remove a *different* worker's entry and strand it. Stale idle-stack entries are the deliberate, accepted design (spec section 4) — every worker still sweeps every shard regardless of signal state, so no work is ever lost; a stray credit only makes that worker's next park attempt a non-blocking no-op.
- `IActivationScheduler`'s public interface (`ValueTask ScheduleAsync(GrainActivation, CancellationToken)`, `IAsyncDisposable`) does not change — this is an internal implementation swap.
- No `.csproj` changes are needed: `System.Collections.Concurrent` and `System.Threading` are already referenced by `Quark.Runtime` and `Quark.Tests.Unit`.
- The new correctness test added in Task 2 must be described honestly (in its own comment and in this plan) as guarding against an *implementation* mistake, not an open race — the push-then-recheck ordering closes the classic lost-wakeup race by construction (spec section 4), unlike the prior round's race, which was a genuinely open bug at design time.

---

## File Structure

```
src/Quark.Runtime/ActivationScheduler.cs                                          (modify — full rewrite of the class body)
tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs  (modify — add one new test)
docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md        (reference only, not modified until Task 3's measured-outcome append)
```

---

### Task 1: Replace the shared wake signal with per-worker semaphores + idle-worker registry

**Files:**
- Modify: `src/Quark.Runtime/ActivationScheduler.cs` (full rewrite of the class body — the file is 307 lines, all replaced by the content in Step 2 below)
- Test: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerTests.cs` (existing, unmodified — used to verify this task's correctness)
- Test: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs` (existing, unmodified — used to verify this task's correctness; Task 2 adds a new test to this same file)

**Interfaces:**
- Consumes: `GrainActivation` (from `src/Quark.Runtime/GrainActivation.cs`) — `GrainId` (property), `TryMarkScheduled()`, `AbortSchedule()`, `SetSchedulerEnqueueTime()`, `TakeSchedulerEnqueueTime()`, `TryBeginDrain()`, `DrainAsync(int drainBudget, CancellationToken)`, `CompleteDrain(ActivationDrainResult)` — all `internal`, unchanged signatures, already used by the current file.
- Consumes: `SiloRuntimeOptions.SchedulerMaxConcurrentActivations` (int), `SchedulerDrainBudget` (int), `SchedulerReadyQueueCapacity` (int), `SchedulerOverloadMode` (enum: `Wait` | `RejectWhenFull`) — unchanged, from `src/Quark.Runtime/SiloRuntimeOptions.cs`.
- Produces: `ActivationScheduler` still implements `IActivationScheduler` (`ValueTask ScheduleAsync(GrainActivation, CancellationToken = default)`, `IAsyncDisposable`) — no signature change. `RunWorkerAsync`'s parameter is renamed from `homeShard` to `workerIndex` (same value: shard count == worker count, unchanged) — internal rename only, not part of any public surface.

- [ ] **Step 1: Confirm no drift on the file before rewriting it**

Run: `git log --oneline -1 -- src/Quark.Runtime/ActivationScheduler.cs`
Expected output: `7aa7ebc Merge branch 'worktree-worktree-workstealing-scheduler'` (or an equivalent commit whose content matches the 307-line file described above) as the most recent commit touching this file. If a materially different file shows up, stop and re-read it before proceeding — the full-file replacement below assumes this exact starting point.

- [ ] **Step 2: Replace the entire file contents**

Replace the full contents of `src/Quark.Runtime/ActivationScheduler.cs` with:

```csharp
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
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Quark.Runtime/Quark.Runtime.csproj`
Expected: `Build succeeded.` with zero errors/warnings.

- [ ] **Step 4: Run the existing scheduler correctness suite**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SchedulingSemantics"`
Expected: all tests pass, including `ActivationSchedulerTests` (11 spec tests, especially `Spec6`/`Spec7`) and both existing tests in `ActivationSchedulerConcurrencyStressTests.cs`. These exercise the rewritten file directly — a correctness regression from the wake-signal swap would surface here.

- [ ] **Step 5: Run it 5 more times to catch timing-sensitive flakiness early**

Run (5 times): `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SchedulingSemantics"`
Expected: pass every time. If `TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost` (the existing 300-iteration single-shard race test) ever fails, that is a genuine regression in the new wake logic — do not proceed to Task 2 until it's clean 5/5.

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Runtime/ActivationScheduler.cs
git commit -m "$(cat <<'EOF'
Shard the scheduler's idle-wake signal into per-worker semaphores

Replace the single shared _workSignal SemaphoreSlim (92% of contended-lock
samples per dotnet-trace after the ConcurrentQueue swap) with one semaphore
per worker plus a lock-free idle-worker registry, so an enqueue's exact
empty->non-empty transition targets exactly one idle worker instead of
releasing an object every worker contends on.
EOF
)"
```

---

### Task 2: Add a targeted wake-path test

**Files:**
- Modify: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs` (add one new `[Fact]` test and update the class-level doc comment)

**Interfaces:**
- Consumes: `ActivationScheduler` (from Task 1), constructed the same way as the file's existing two tests: `new ActivationScheduler(options)` where `options` is a `SiloRuntimeOptions` with `SchedulerMaxConcurrentActivations` set.
- Consumes: `GrainActivation` — same constructor signature already used in this file: `new GrainActivation(GrainId, GrainType, isReentrant: false, IServiceProvider, ILogger<GrainActivation>)`, plus `PostAsync(Func<ValueTask>)` and `DisposeAsync()`.
- Produces: nothing consumed by later tasks — this is a leaf test file.

- [ ] **Step 1: Read the current file to confirm no drift**

Run: `git log --oneline -1 -- tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs`
Expected: the commit that added `TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost` (the file currently ends at line 162 with that test's closing braces). If the file differs, re-read it before editing.

- [ ] **Step 2: Update the class-level doc comment**

In `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs`, replace the existing class-level `<summary>` comment (currently ending with "...which the general stress test above does not reliably rule out.") with:

```csharp
/// <summary>
///     Stress-tests the ConcurrentQueue-per-worker ready queue's correctness under many concurrent
///     producers and a small consumer worker count -- specifically, that the cross-shard steal sweep
///     never loses a scheduled activation's work under contention. This is the risk introduced by
///     swapping Channel&lt;T&gt; for ConcurrentQueue&lt;T&gt;
///     (docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md) that the rest of the
///     SchedulingSemantics suite doesn't directly exercise: those tests check ordering and fairness
///     with a handful of named grains, not raw concurrent-producer throughput.
///     A second, more targeted test below covers the single-shard/no-rescue-sweep case: with sustained
///     multi-shard traffic, a missed wake-up is almost always rescued by an unrelated wake elsewhere,
///     which the general stress test above does not reliably rule out.
///     A third test below exercises the per-worker-semaphore/idle-registry wake path added in
///     docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md. Unlike the second
///     test, this one is not chasing an open race: the push-then-recheck ordering that path uses closes
///     the classic lost-wakeup race by construction (see that spec's section 4). This test instead
///     guards against an implementation mistake -- wrong ordering of the push/recheck steps, or wrong
///     volatile/interlocked semantics on the double-check -- regressing that closed race back open.
/// </summary>
```

- [ ] **Step 3: Add the new test**

Add the following `[Fact]` method to `ActivationSchedulerConcurrencyStressTests.cs`, immediately after the closing brace of `TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost` (before the class's final closing brace):

```csharp
    [Fact]
    public async Task SingleWorker_RepeatedIdleParkCycles_NeverStrandsWork()
    {
        // Exercises the per-worker-semaphore/idle-registry wake path
        // (docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md) rather than the
        // ready-queue collection itself. workerCount=1 makes the idle-stack trivially either empty or
        // contain exactly that one worker, so every idle transition in this test exercises the full
        // push -> re-sweep (double-check) -> park sequence. Unlike
        // TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost above, this is not chasing
        // an open race -- the push-then-recheck ordering closes the classic lost-wakeup race by
        // construction (spec section 4). This test instead guards against an implementation mistake
        // (wrong step ordering, wrong volatile/interlocked semantics) regressing that closed race.
        const int iterations = 300;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var options = new SiloRuntimeOptions
            {
                ClusterId = "test",
                ServiceId = "scheduler-wake-signal-stress",
                SiloName = "silo0",
                SchedulerMaxConcurrentActivations = 1,
            };

            await using var scheduler = new ActivationScheduler(options);
            services.AddSingleton<IActivationScheduler>(scheduler);
            await using ServiceProvider root = services.BuildServiceProvider();

            var activation = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"idle-park-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance);

            int completed = 0;

            // PostAsync awaits the posted work item's own completion (see the comment on
            // GrainActivation.PostAsync's use in RunDeactivationAsync, ~line 372 of
            // GrainActivation.cs: "PostAsync awaits the work item's completion"), so each
            // sequential await below only returns once the single worker has fully drained that
            // item and returned to the top of its loop -- meaning it has gone through at least
            // one push -> re-sweep -> (park or immediately-find-more-work) cycle between each of
            // these three posts, with no wall-clock delay needed to force that ordering. This
            // matches the no-timing-based-synchronization standard the rest of this codebase's
            // scheduling tests already follow (see e.g. ReentrantTests' gate-based rewrite).
            for (int post = 0; post < 3; post++)
            {
                await activation.PostAsync(() =>
                {
                    Interlocked.Increment(ref completed);
                    return ValueTask.CompletedTask;
                }).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            }

            Assert.True(completed == 3,
                $"Iteration {iteration}: expected 3 completions, got {completed} -- work was stranded " +
                "in a single-worker idle-park cycle.");

            await activation.DisposeAsync();
        }
    }
```

- [ ] **Step 4: Run the new test alone**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SingleWorker_RepeatedIdleParkCycles_NeverStrandsWork"`
Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 5: Verify this test actually exercises the intended path (not a vacuous pass)**

Temporarily break the double-check by editing `src/Quark.Runtime/ActivationScheduler.cs`'s `RunWorkerAsync`: comment out the `activation = TryDequeueAny(ref cursor);` re-sweep line (the one immediately after `_idleWorkers.Push(workerIndex);`), so a worker parks without re-checking after registering idle. Run the new test:

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SingleWorker_RepeatedIdleParkCycles_NeverStrandsWork"`
Expected: this specific edit should NOT change the test's pass/fail outcome by itself (the targeted-release-via-TryPop path still delivers the wake even without the double-check re-sweep, since `_idleWorkers.Push` still ran before parking) — this step exists to confirm that observation explicitly rather than assume it. Record the actual result in the task report either way; if it unexpectedly fails, that's useful signal about which half of the mechanism the test is really covering. Revert the temporary edit immediately after this step (`git checkout -- src/Quark.Runtime/ActivationScheduler.cs`) regardless of the outcome — do not leave the broken version in place.

- [ ] **Step 6: Run the full SchedulingSemantics suite once more**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SchedulingSemantics"`
Expected: all tests pass, including the two pre-existing stress tests and the new one.

- [ ] **Step 7: Commit**

```bash
git add tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs
git commit -m "$(cat <<'EOF'
Add a targeted test for the per-worker idle-park wake cycle

Exercises the push -> re-sweep -> park sequence added for the
per-worker-semaphore/idle-registry wake signal. Documented honestly as
guarding an implementation mistake, not an open race -- the double-check
closes the classic lost-wakeup race by construction.
EOF
)"
```

---

### Task 3: Full verification — test suites, CPU-pinned PingPong re-benchmark, dotnet-trace re-profile, AstroSim regression check

This task is execution/measurement only — no new code, except the "Measured outcome" section appended to the spec in Step 7. Do it directly rather than delegating to a fresh subagent that lacks the profiling methodology context from the prior rounds of this investigation.

**Files:** none created or modified except:
- Modify: `docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md` (append "Measured outcome" section)
- Create: `.superpowers/sdd/task-3-report.md` (or equivalent progress-tracking location used by the executing skill)

- [ ] **Step 1: Full unit test suite, 3 runs**

```bash
dotnet build Quark.slnx -c Release
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
```

Expected: each run shows only known pre-existing flaky-test failures (cross-check any failure's test name against `project_flaky_tests` memory at `/home/nvthanh/.claude/projects/-home-nvthanh-works-Quark/memory/project_flaky_tests.md` before treating it as a regression — re-run that specific test in isolation with `--filter "FullyQualifiedName~<TestName>"` to confirm it passes alone). Zero `SchedulingSemantics` failures across all 3 runs, full stop — those are not on the flaky list and any failure there is a real regression to investigate, not something to wave through.

- [ ] **Step 2: Full integration test suite**

```bash
dotnet build tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj -c Release
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj -c Release --no-build --filter "category!=integration"
```

Expected: `Passed!` with 0 failures, modulo the already-documented pre-existing `ReminderIntegrationTests.UnregisterReminder_StopsFutureFirings` flakiness (re-run in isolation 3-5x per memory before treating as a regression).

- [ ] **Step 3: Set up a CPU-pinned control comparison**

```bash
git worktree add /tmp/quark-control-7aa7ebc 7aa7ebc
cd /tmp/quark-control-7aa7ebc
dotnet build tests/Quark.Performance/Quark.Performance.csproj -c Release
cd -
dotnet build tests/Quark.Performance/Quark.Performance.csproj -c Release
```

Check available cores and pick a fixed, disjoint pair of core sets for the two builds:

```bash
nproc
```

Expected: this box has historically shown 32 cores. Reserve e.g. cores 0-7 for the work-stealing-wake-signal build and cores 8-15 for the control build (adjust if `nproc` differs) — disjoint sets so the two processes cannot compete with each other, only with whatever other load the rest of the box carries.

- [ ] **Step 4: Run 10 interleaved, CPU-pinned paired PingPong trials**

For `trial` in `1..10`, alternate:

```bash
taskset -c 0-7 dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll PingPong --pairs 32 --duration 20
taskset -c 8-15 dotnet /tmp/quark-control-7aa7ebc/tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll PingPong --pairs 32 --duration 20
```

Record each trial's "Akka-comparable rate (x2)" number into two lists (this-design, control). Compute and record median and spread (min/max) for each list.

- [ ] **Step 5: Re-profile with dotnet-trace**

```bash
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll PingPong --pairs 32 --duration 25 &
# find the PID via: ps aux | grep "Quark.Performance.dll" | grep -v grep
dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time --duration 00:00:15 -o pingpong_wakesignal.nettrace
dotnet-trace convert pingpong_wakesignal.nettrace --format speedscope -o pingpong_wakesignal.speedscope.json
```

`dotnet-trace convert` appends `.speedscope.json` to whatever `-o` name you gave it — check with `ls *.json` and use the real filename below.

Save the following as `analyze_trace.py` in the same directory as the speedscope JSON (this is the exact, already-verified script from the prior round — the `call_count.get('Monitor.Enter_Slowpath', 0)` line is known to under-report as 0 due to a dict-key mismatch; use the parent-frame table it prints instead):

```python
import json
import sys
from collections import defaultdict, Counter

path = sys.argv[1]
with open(path) as f:
    data = json.load(f)

frames = data['shared']['frames']
self_time = defaultdict(float)
call_count = defaultdict(int)
parent_of_monitor = Counter()
total_duration = 0.0
thread_count = 0

for profile in data['profiles']:
    if profile.get('type') != 'evented':
        continue
    thread_count += 1
    stack = []  # list of [frame_idx, start_at, accumulated_children_time]
    name_stack = []
    for ev in profile['events']:
        t = ev['type']
        at = ev['at']
        frame_idx = ev['frame']
        name = frames[frame_idx]['name']
        if t == 'O':
            stack.append([frame_idx, at, 0.0])
            name_stack.append(name)
            if 'Monitor.Enter_Slowpath' in name and len(name_stack) >= 2:
                parent_of_monitor[name_stack[-2]] += 1
        elif t == 'C':
            if not stack:
                continue
            f_idx, start_at, child_time = stack.pop()
            name_stack.pop()
            dur = max(0.0, at - start_at)
            self_dur = max(0.0, dur - child_time)
            self_time[frames[f_idx]['name']] += self_dur
            call_count[frames[f_idx]['name']] += 1
            if stack:
                stack[-1][2] += dur
    if profile.get('endValue') is not None:
        total_duration += profile['endValue']

print(f"Threads analyzed: {thread_count}")
print(f"Total sampled thread-time: {total_duration:.1f} ms\n")

unmanaged = self_time.get('UNMANAGED_CODE_TIME', 0.0)
cpu = self_time.get('CPU_TIME', 0.0)
print(f"UNMANAGED_CODE_TIME: {unmanaged:.1f} ms ({100*unmanaged/total_duration:.1f}% of total)")
print(f"CPU_TIME: {cpu:.1f} ms ({100*cpu/total_duration:.1f}% of total)")
print(f"Monitor.Enter_Slowpath sample count (unreliable, see parent-frame table): {call_count.get('Monitor.Enter_Slowpath', 0)}\n")

print("=== Parent frame when Monitor.Enter_Slowpath opens ===")
for name, c in parent_of_monitor.most_common(15):
    print(f"{c:8d}  {name}")

print("\n=== Top 20 self-time frames overall ===")
for name, t in sorted(self_time.items(), key=lambda kv: -kv[1])[:20]:
    print(f"{t:10.1f} ms ({100*t/total_duration:5.1f}%)  {name}")
```

Run: `python3 analyze_trace.py <the-actual-speedscope-filename>.json`

Record:
- `UNMANAGED_CODE_TIME` as a percentage of total (compare against the prior round's 86.4%).
- Total contended-lock-acquire attempts, summed from the parent-frame table (compare against the prior round's 70,100).
- The parent-frame breakdown itself: has `_workerSignals`/`SemaphoreSlim.Release`/`RunWorkerAsync`'s share dropped from the prior round's combined ~92%? Do `ConcurrentStack` frames appear at all, and if so, what share?

- [ ] **Step 6: AstroSim regression check**

```bash
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll AstroSim --bodies 1000000 --grid 16 --duration 15
```

Expected: no crash, no hang, throughput in the same order of magnitude as the prior round's 129,700 msg/s (this design) / 134,941 msg/s (control) pairing — this is a regression check, not a benchmark this change is expected to improve (AstroSim's real per-tick work doesn't stress the ready-queue's empty-oscillation pattern the way PingPong does).

Clean up the control worktree once all measurements are recorded:

```bash
git worktree remove /tmp/quark-control-7aa7ebc --force
```

- [ ] **Step 7: Write the honest result into the design spec**

Add a "## 8. Measured outcome (implemented and profiled)" section to `docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md`, following the same pattern as the prior two specs' own measured-outcome sections: report the actual median/spread PingPong numbers for both sides, the trace analysis findings from Step 5, and the AstroSim regression-check result.

Evaluate explicitly against the spec's own section 6 success criteria: (a) did the paired trials show a consistent, non-sign-flipping improvement, (b) did `Monitor.Enter_Slowpath` count measurably drop, (c) were all correctness tests clean. If any criterion isn't met, say so plainly and explain what the data actually shows — do not round up or bury a disappointing or ambiguous number, the same standard the prior two rounds' specs both held to.

- [ ] **Step 8: Commit**

```bash
git add docs/superpowers/specs/2026-07-09-scheduler-wake-signal-sharding-design.md
git commit -m "$(cat <<'EOF'
Record measured outcome of the per-worker wake-signal sharding change

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Finishing

After Task 3, follow `superpowers:finishing-a-development-branch` to decide how to integrate the work. This is a real decision point, not a formality: per the spec's own success criteria (section 6), this change should only ship as the new default if the measured outcome actually clears the bar (consistent throughput improvement AND a measurable `Monitor.Enter_Slowpath` drop AND clean correctness). If Task 3's honest measurement doesn't clear that bar, say so explicitly before offering the finishing-a-development-branch menu, and let the human decide whether to merge anyway (e.g. for the trace-evidence value alone), keep the branch for further iteration, or discard — do not default to merging just because the code compiles and tests pass, the way the two prior rounds' correctness-clean-but-throughput-inconclusive results were still explicitly merged only after being reported honestly first.

This session's established pattern (do not deviate without asking): merge to local `main` only, run the full test suite once more post-merge, verify the build, then ask the user whether to push — do not push to `origin` without an explicit separate ask.
