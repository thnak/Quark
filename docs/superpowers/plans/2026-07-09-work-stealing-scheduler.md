# Work-Stealing ActivationScheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ActivationScheduler`'s `Channel<GrainActivation>[]`-per-shard ready queue with `ConcurrentQueue<GrainActivation>[]`-per-shard, to remove `Channel<T>`'s async-waiter/continuation overhead from the hot dispatch path while preserving every existing scheduling correctness guarantee.

**Architecture:** Same shape as the currently-shipped sharded scheduler (commit `07ecd51`): N worker queues (N = `SchedulerMaxConcurrentActivations`), activations hash-placed by `GrainId`, every worker sweeps every shard (own first, then round-robin) rather than owning one exclusively, plus a shared `SemaphoreSlim` idle-wake signal gated on empty→non-empty transitions. Only the underlying per-shard collection changes: `ConcurrentQueue<T>` instead of `Channel<T>`.

**Tech Stack:** .NET 10, `System.Collections.Concurrent.ConcurrentQueue<T>`, `System.Threading.SemaphoreSlim`, xUnit.

## Global Constraints

- Do not change `GrainActivation`'s mailbox channel, `TryBeginDrain`/`CompleteDrain`, drain-budget/fairness-yield logic, or any diagnostics event shape — all orthogonal to this change per the spec's non-goals (`docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md` section 6).
- Do not introduce a hand-rolled lock-free deque (e.g. Chase-Lev) or piggyback on the CLR `ThreadPool` — both were considered and explicitly not chosen (spec section 2).
- `SchedulerReadyQueueCapacity` / `SchedulerOverloadMode` bounded-queue behavior must only cost anything when `SchedulerReadyQueueCapacity > 0` (the non-default, opt-in case) — the common unbounded path must not allocate or touch a capacity gate at all.
- The reschedule-after-partial-drain path's behavior under a full bounded capacity gate must match the pre-existing (pre-this-change) design exactly: silently skip the re-enqueue (no `AbortSchedule()` call, no exception) rather than blocking or throwing — this is pre-existing behavior being preserved, not a new fix, and is out of scope to change here.
- `IActivationScheduler`'s public interface (`ValueTask ScheduleAsync(GrainActivation, CancellationToken)`, `IAsyncDisposable`) does not change — this is an internal implementation swap.
- No `.csproj` changes are needed: `System.Collections.Concurrent` and `System.Threading` are part of the BCL already referenced by `Quark.Runtime` and `Quark.Tests.Unit`.

---

## File Structure

```
src/Quark.Runtime/ActivationScheduler.cs                                          (modify — full rewrite of the class body)
tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs  (create — new stress test)
docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md               (reference only, not modified)
```

---

### Task 1: Swap Channel-per-shard for ConcurrentQueue-per-shard in ActivationScheduler

**Files:**
- Modify: `src/Quark.Runtime/ActivationScheduler.cs` (full rewrite of the class body — the file is 289 lines, all of which are replaced by the content in Step 3 below)
- Test: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerTests.cs` (existing, unmodified — used to verify this task)

**Interfaces:**
- Consumes: `GrainActivation` (from `src/Quark.Runtime/GrainActivation.cs`) — specifically `GrainId` (property), `TryMarkScheduled()`, `AbortSchedule()`, `SetSchedulerEnqueueTime()`, `TakeSchedulerEnqueueTime()`, `TryBeginDrain()`, `DrainAsync(int drainBudget, CancellationToken)`, `CompleteDrain(ActivationDrainResult)` — all `internal`, unchanged signatures, already used by the current file.
- Consumes: `SiloRuntimeOptions.SchedulerMaxConcurrentActivations` (int), `SchedulerDrainBudget` (int), `SchedulerReadyQueueCapacity` (int), `SchedulerOverloadMode` (enum: `Wait` | `RejectWhenFull`) — unchanged, from `src/Quark.Runtime/SiloRuntimeOptions.cs`.
- Produces: `ActivationScheduler` still implements `IActivationScheduler` (`ValueTask ScheduleAsync(GrainActivation, CancellationToken = default)`, `IAsyncDisposable`) — no signature change, so `LocalGrainCallInvoker`, `GrainActivation`, and all DI registration in `RuntimeServiceCollectionExtensions.AddQuarkRuntime` need no changes.

- [ ] **Step 1: Read the current file to confirm no drift**

Run: `git log --oneline -1 -- src/Quark.Runtime/ActivationScheduler.cs`
Expected output: `07ecd51 Shard the scheduler ready queue to reduce lock contention (+7%)` as the most recent commit touching this file. If a different/newer commit shows up, stop and re-read the file before proceeding — the diff below assumes this exact starting point.

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
/// </remarks>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly ConcurrentQueue<GrainActivation>[] _shards;

    // Per-shard capacity gate, only allocated when SchedulerReadyQueueCapacity > 0. ConcurrentQueue<T>
    // has no native capacity limit (unlike the Channel<T> it replaces), so bounded-queue backpressure
    // is layered on top via a counting semaphore: ScheduleAsync acquires a slot before enqueueing,
    // RunWorkerAsync releases one after a successful dequeue. Null in the default unbounded case, so
    // the common path (PingPong, AstroSim, most workloads) pays nothing for this.
    private readonly SemaphoreSlim[]? _capacityGates;

    // Idle-wake signal: released only on a shard's empty->non-empty transition (ConcurrentQueue.Count
    // == 1 right after Enqueue -- a best-effort, not exact, check: it can race with a concurrent
    // dequeue on the same shard and occasionally double- or under-release. That's safe by
    // construction, not just acceptable -- an extra Release() just wakes a worker slightly early (it
    // re-sweeps, finds nothing, waits again); a missed Release() cannot strand work, because any
    // worker that later goes idle re-sweeps every shard from scratch before waiting again, and will
    // find the item. SemaphoreSlim.Release()/WaitAsync() never lose a wakeup regardless of ordering.
    // See docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md section 4.
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
        if (shard.Count == 1)
            _workSignal.Release();

        QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
        _diagnostics.OnSchedulerReadyQueueDepthChanged(
            new SchedulerReadyQueueDepthChangedEvent(shard.Count, 1));
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
                if (_shards[cursor].Count > 0 && _shards[cursor].TryDequeue(out GrainActivation? candidate))
                {
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
                new SchedulerReadyQueueDepthChangedEvent(_shards[cursor].Count, -1));

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

- [ ] **Step 3: Build the runtime project**

Run: `dotnet build src/Quark.Runtime/Quark.Runtime.csproj -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Build the full solution**

Run: `dotnet build Quark.slnx -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Run the existing SchedulingSemantics suite 5 times**

Run this exact command 5 times in a row (not just once — these tests are timing-sensitive and this is exactly what would catch a correctness regression from the collection swap):

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~SchedulingSemantics" --no-build
```

Expected each time: `Passed! - Failed: 0, Passed: 27, Skipped: 0, Total: 27` (27 is the current count of tests under `SchedulingSemantics/` as of this plan; if it differs, some other change added/removed tests — investigate before proceeding, don't just accept a different number).

If `Spec6_SchedulerConcurrencyCap_MaxOne_BlocksSecondActivation` or `Spec7_SchedulerConcurrencyParallelism_MaxTwo_AllowsConcurrentActivations` fail even once across the 5 runs, STOP — this means the ConcurrentQueue swap broke the N-way concurrency liveness guarantee (the exact risk this plan's Global Constraints and the design spec section 3 call out). Do not proceed to commit; re-examine the sweep logic in `RunWorkerAsync` before continuing.

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Runtime/ActivationScheduler.cs
git commit -m "$(cat <<'EOF'
Replace Channel<T>-per-shard scheduler ready queue with ConcurrentQueue<T>

ConcurrentQueue<T> is a proven, already-in-the-BCL lock-free MPMC
structure with none of Channel<T>'s async-waiter/continuation
machinery, which profiling of the prior sharded-Channel design (07ecd51)
showed was being paid even for plain non-blocking dequeue attempts.
Same architecture otherwise: N worker queues, GrainId-hashed placement,
every worker sweeps every shard (not statically owned) to preserve the
SchedulerMaxConcurrentActivations concurrency-liveness guarantee, shared
gated SemaphoreSlim for idle wake. Bounded-capacity backpressure
(SchedulerReadyQueueCapacity>0, the non-default opt-in case) is now
layered on via a per-shard capacity-counting semaphore since
ConcurrentQueue<T> has no native bound.

See docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add a concurrent-producer stress test for the ready queue

**Files:**
- Create: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs`

**Interfaces:**
- Consumes: `ActivationScheduler` (public constructor `ActivationScheduler(SiloRuntimeOptions, IQuarkDiagnosticListener?, DiagnosticOptions?)` from Task 1 — unchanged signature), `IActivationScheduler`, `GrainActivation` (public constructor, `PostAsync(Func<ValueTask>)`, `DisposeAsync()`), `SiloRuntimeOptions`, `GrainId`, `GrainType` — all already used elsewhere in `tests/Quark.Tests.Unit/SchedulingSemantics/`.
- Produces: nothing consumed by later tasks — this is a leaf test file.

- [ ] **Step 1: Write the stress test**

Create `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Stress-tests the ConcurrentQueue-per-worker ready queue's correctness under many concurrent
///     producers and a small consumer worker count -- specifically, that the cross-shard steal sweep
///     never loses a scheduled activation's work under contention. This is the risk introduced by
///     swapping Channel&lt;T&gt; for ConcurrentQueue&lt;T&gt;
///     (docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md) that the rest of the
///     SchedulingSemantics suite doesn't directly exercise: those tests check ordering and fairness
///     with a handful of named grains, not raw concurrent-producer throughput.
/// </summary>
public sealed class ActivationSchedulerConcurrencyStressTests
{
    [Fact]
    public async Task ManyConcurrentProducers_AcrossManyActivations_NoWorkIsLost()
    {
        const int workerCount = 2; // deliberately small -- forces real cross-shard stealing
        const int activationCount = 64;
        const int postsPerActivation = 25;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "scheduler-stress",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
        };

        await using var scheduler = new ActivationScheduler(options);
        services.AddSingleton<IActivationScheduler>(scheduler);
        await using ServiceProvider root = services.BuildServiceProvider();

        var activations = new GrainActivation[activationCount];
        for (int i = 0; i < activationCount; i++)
        {
            activations[i] = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"stress-{i}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance);
        }

        var completedCounts = new int[activationCount];
        var currentlyExecuting = new int[activationCount];
        var overlapDetected = new ConcurrentBag<int>();

        var postTasks = new List<Task>(activationCount * postsPerActivation);
        for (int a = 0; a < activationCount; a++)
        {
            int activationIndex = a;
            for (int p = 0; p < postsPerActivation; p++)
            {
                postTasks.Add(Task.Run(() => activations[activationIndex].PostAsync(() =>
                {
                    if (Interlocked.CompareExchange(ref currentlyExecuting[activationIndex], 1, 0) != 0)
                        overlapDetected.Add(activationIndex);

                    Interlocked.Exchange(ref currentlyExecuting[activationIndex], 0);
                    Interlocked.Increment(ref completedCounts[activationIndex]);

                    return ValueTask.CompletedTask;
                }).AsTask()));
            }
        }

        await Task.WhenAll(postTasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(overlapDetected);
        for (int i = 0; i < activationCount; i++)
        {
            Assert.True(completedCounts[i] == postsPerActivation,
                $"Activation {i} completed {completedCounts[i]}/{postsPerActivation} posts -- work was lost.");
        }

        foreach (GrainActivation activation in activations)
            await activation.DisposeAsync();
    }
}
```

- [ ] **Step 2: Build the test project**

Run: `dotnet build tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run the new test 5 times**

Run this exact command 5 times in a row (concurrency stress tests can be flaky-in-either-direction — a single pass doesn't prove much):

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~ActivationSchedulerConcurrencyStressTests" --no-build
```

Expected each time: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

If it fails even once, STOP. Read the failure message carefully:
- `Assert.Empty(overlapDetected)` failing means two concurrent drains ran for the same activation at once — a real regression in `TryBeginDrain`'s exclusivity (should be structurally impossible; if this fires, the bug is almost certainly in this test's harness, not the scheduler, since `TryBeginDrain` is unchanged by Task 1 — but verify, don't assume).
- A `completedCounts[i] != postsPerActivation` failure means work was genuinely lost by the `ConcurrentQueue`-based ready queue under concurrent producer load — this is exactly the risk this test exists to catch. Do not proceed to commit; re-examine `EnqueueToShard`'s 0→1 gating and `RunWorkerAsync`'s sweep in `ActivationScheduler.cs`.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerConcurrencyStressTests.cs
git commit -m "$(cat <<'EOF'
Add concurrent-producer stress test for the sharded ready queue

Many threads calling ScheduleAsync concurrently across a small worker
count (2), asserting every scheduled activation is drained exactly
once with no lost or overlapping drains. Covers the specific risk the
ConcurrentQueue<T> swap (previous commit) introduces that the rest of
SchedulingSemantics doesn't exercise -- raw concurrent-producer load,
not ordering/fairness with a handful of named grains.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Full verification — test suites, PingPong re-profile, AstroSim regression check

This task is execution/measurement only — no new code. Do it directly rather than delegating to a fresh subagent that lacks the profiling methodology context from the prior two rounds of this investigation.

**Files:** none created or modified (except the report below).
- Create: `.superpowers/sdd/task-3-report.md` (or equivalent progress-tracking location used by the executing skill) with the results.

- [ ] **Step 1: Full unit test suite, 3 runs**

```bash
dotnet build Quark.slnx -c Release
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj -c Release --no-build
```

Expected: each run shows only known pre-existing flaky-test failures (cross-check any failure's test name against the `project_flaky_tests` memory file at
`/home/nvthanh/.claude/projects/-home-nvthanh-works-Quark/memory/project_flaky_tests.md` before treating it as a regression — re-run that specific test in isolation with `--filter "FullyQualifiedName~<TestName>"` to confirm it passes alone). Zero `SchedulingSemantics` failures across all 3 runs, full stop — those are not on the flaky list and any failure there is a real regression to investigate, not something to wave through.

- [ ] **Step 2: Full integration test suite**

```bash
dotnet build tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj -c Release
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj -c Release --no-build --filter "category!=integration"
```

Expected: `Passed!` with 0 failures (this filter excludes Redis-backed tests that need Testcontainers infrastructure).

- [ ] **Step 3: Re-run the PingPong benchmark**

```bash
dotnet build tests/Quark.Performance/Quark.Performance.csproj -c Release
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll PingPong --pairs 32 --duration 20
```

Record the final "Akka-comparable rate (x2)" number. Compare against the two prior baselines: 621,290 msg/s (pre-sharding), 665,962 msg/s (shipped sharded-Channel fix, commit `07ecd51`).

- [ ] **Step 4: Re-profile with dotnet-trace to confirm the contention story actually changed**

```bash
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll PingPong --pairs 32 --duration 25 &
# find the PID via: ps aux | grep "Quark.Performance.dll" | grep -v grep
dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time --duration 00:00:15 -o pingpong_workstealing.nettrace
dotnet-trace convert pingpong_workstealing.nettrace --format speedscope -o pingpong_workstealing.speedscope.json
```

`dotnet-trace convert` writes the actual output filename with `.speedscope.json` appended to whatever `-o` name you gave it (e.g. `-o foo.speedscope.json` produces `foo.speedscope.speedscope.json`) — check with `ls *.json` and use the real filename below.

Save this as `analyze_trace.py` in the same directory as the speedscope JSON, then run `python3 analyze_trace.py <the-actual-speedscope-filename>.json`:

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

# unit is 'milliseconds' per speedscope profile metadata -- no conversion needed
print(f"Threads analyzed: {thread_count}")
print(f"Total sampled thread-time: {total_duration:.1f} ms\n")

unmanaged = self_time.get('UNMANAGED_CODE_TIME', 0.0)
cpu = self_time.get('CPU_TIME', 0.0)
print(f"UNMANAGED_CODE_TIME: {unmanaged:.1f} ms ({100*unmanaged/total_duration:.1f}% of total)")
print(f"CPU_TIME: {cpu:.1f} ms ({100*cpu/total_duration:.1f}% of total)")
print(f"Monitor.Enter_Slowpath sample count: {call_count.get('Monitor.Enter_Slowpath', 0)}\n")

print("=== Parent frame when Monitor.Enter_Slowpath opens ===")
for name, c in parent_of_monitor.most_common(15):
    print(f"{c:8d}  {name}")
```

Report:
- Total `UNMANAGED_CODE_TIME` as a percentage of total sampled thread-time.
- `Monitor.Enter_Slowpath` sample count, compared against the prior two data points (~75,300 baseline single-queue design, ~67,900 final sharded-`Channel<T>` design from commit `07ecd51`).
- The parent-frame breakdown of `Monitor.Enter_Slowpath` — confirm whether `ConcurrentQueue<T>`-internal frames still show up at all (they may not — `ConcurrentQueue<T>` is largely lock-free), and if they do, how their share compares to the prior `Channel<T>`-internal frames.

- [ ] **Step 5: AstroSim regression check**

```bash
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll AstroSim --bodies 1000000 --grid 16 --duration 15
```

Expected: no crash, no hang, sustained throughput in the same order of magnitude as the prior run recorded in this conversation (206,539 msg/s at this exact scale) — this is a regression check, not a benchmark this change is expected to improve, since AstroSim's real per-tick work between calls doesn't stress the ready-queue's empty-oscillation pattern the way PingPong does.

- [ ] **Step 6: Write the honest result into the design spec**

Add a "Measured outcome" section to `docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md` (same pattern as section 6 of the prior sharded-Channel spec) reporting the actual PingPong number, the trace analysis findings from Step 4, and the AstroSim regression-check result. If the result falls short of expectations, say so plainly and explain why, the same way the prior spec's "Measured outcome" section did — do not round up or bury a disappointing number.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md
git commit -m "$(cat <<'EOF'
Record measured outcome of the ConcurrentQueue-based work-stealing scheduler

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Finishing

After Task 3, follow `superpowers:finishing-a-development-branch` to decide how to integrate the work (this repo's established pattern this session: merge to local `main`, run the full test suite once more post-merge, verify the build, then ask the user whether to push — do not push without an explicit ask, per this session's established pattern).
