# Work-stealing ActivationScheduler design

## 1. Background

The sharded-ready-queue fix (`docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md`,
commit `07ecd51`, issue #161) shipped a real but modest +7% throughput improvement on the PingPong
benchmark (621K → 666K msg/s). Its own "Measured outcome" section explains the ceiling: preserving
`SchedulerMaxConcurrentActivations`'s concurrency-liveness guarantee (any N distinct busy activations
run truly concurrently — `ActivationSchedulerTests.Spec6`/`Spec7`) required every worker to be able to
reach every shard's work, which reintroduced cross-shard synchronization almost as expensive as the
single shared queue it replaced. `Monitor.Enter_Slowpath` sample counts barely moved across three
implementation iterations of that fix (~75K baseline → ~68K final).

This spec designs a genuine work-stealing scheduler to beat that ceiling, informed by two research
findings (see conversation record, not reproduced here):

- **Orleans doesn't hand-roll a work-stealing deque either.** Grain turns are dispatched through a
  lightweight per-activation `TaskScheduler` that ultimately rides the CLR's own `ThreadPool`, which
  already does work-stealing internally. Single-threaded-per-activation execution is enforced by a
  scheduling flag, not by which physical thread runs the turn — the same pattern Quark's
  `GrainActivation.TryBeginDrain`/`CompleteDrain` already implements.
- **A textbook Chase-Lev deque is a poor fit for Quark's actual access pattern.** Chase-Lev assumes
  the producer of new work is usually one of the consumer threads itself (recursive fork-join: a
  running task spawns children onto its own deque). Quark's `ScheduleAsync` is called from arbitrary
  external threads (any grain-to-grain call, any gateway thread) — a multi-producer/multi-consumer
  load-balancing problem, not fork-join. Chase-Lev doesn't cleanly support concurrent cross-thread
  *push*, only cross-thread *steal* (pop).

That second point also explains part of why the shipped fix's ceiling was so low: `Channel<T>` carries
an async-waiter/continuation-registration machinery (built for the "consumer awaits" use case) that
gets paid even for a plain non-blocking dequeue attempt on a non-empty shard. `ConcurrentQueue<T>` — a
proven, already-in-the-BCL, genuinely lock-free MPMC structure with none of that machinery — is a
better-fitted data structure for the same architecture already built, without hand-rolling new
lock-free code.

## 2. Decision: ConcurrentQueue-per-worker, not a hand-rolled deque

Three candidates were considered:

1. **ConcurrentQueue-per-worker + steal** (chosen) — same architecture as the shipped fix, but
   `Channel<GrainActivation>` replaced with `ConcurrentQueue<GrainActivation>`. Low implementation
   risk (no new lock-free code), directly targets the specific overhead source the shipped fix's
   profiling identified.
2. **Hand-rolled Chase-Lev deque per worker** — highest theoretical ceiling (owner-local push/pop can
   be near lock-free), highest risk: genuinely subtle lock-free code (there is an academic paper on
   formally verifying Chase-Lev's correctness in concurrent separation logic), in a codebase that has
   had a real pooled-object concurrency bug before ([[project_mailbox_workitem_pool_race]]). Also a
   structural mismatch per the research finding above (external multi-producer push isn't what
   Chase-Lev is built for). Not chosen for this pass.
3. **Piggyback on the CLR ThreadPool's own work-stealing** — what Orleans actually does. Lowest code
   footprint, most battle-tested underlying implementation, but least custom control over the
   fairness/QoS knobs Quark's scheduler already exposes (`SchedulerDrainBudget`,
   `SchedulerReadyQueueCapacity`, `SchedulerOverloadMode`). Not chosen for this pass — worth
   revisiting if option 1 doesn't clear a meaningful bar.

Deployment: this replaces `ActivationScheduler` as the default (not shipped as an opt-in alternate),
same as the prior fix, once it clears full correctness verification.

## 3. Architecture

N worker queues (N = `SchedulerMaxConcurrentActivations`, meaning unchanged), each a
`ConcurrentQueue<GrainActivation>` instead of a `Channel<GrainActivation>`. Everything else about the
shape carries over from the shipped fix:

- Activations are hashed by `GrainId` to a home queue for their lifetime (`ShardFor`, unchanged
  logic: `(activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _shards.Length`).
- Every worker sweeps **every** queue — own queue first (staggered starting index per worker, as
  today), then others round-robin — rather than owning one exclusively. This is still what preserves
  the "N workers configured means N activations can truly run concurrently" guarantee regardless of
  hash collisions; sharding only changes which structure a given push/pop touches, never which worker
  may service a given activation.
- The owner's "check my own queue" path and another worker's "steal from your queue" path are both
  plain `ConcurrentQueue<T>.TryDequeue()` calls — no `Monitor`, no waiter registration, on either
  side, and no different code path for "owner" vs "thief" (unlike Chase-Lev's asymmetric bottom/top
  design — a deliberate simplification, since Quark's access pattern doesn't have a privileged
  owner-only push path to exploit: pushes are external and concurrent regardless of who's asking).

```csharp
private readonly ConcurrentQueue<GrainActivation>[] _shards;
private readonly SemaphoreSlim _workSignal = new(0, int.MaxValue);

private int ShardFor(GrainActivation activation)
    => (activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _shards.Length;

public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken ct = default)
{
    if (!activation.TryMarkScheduled()) return ValueTask.CompletedTask;
    activation.SetSchedulerEnqueueTime();
    int shardIndex = ShardFor(activation);
    _shards[shardIndex].Enqueue(activation);
    if (_shards[shardIndex].Count == 1) // best-effort 0->1 hint, see §4
        _workSignal.Release();
    // ... existing diagnostics/metrics ...
    return ValueTask.CompletedTask; // no capacity gating in the unbounded default -- see §4
}

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
            if (ct.IsCancellationRequested) return;
            try { await _workSignal.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            continue;
        }
        // ... existing TryBeginDrain / DrainAsync / CompleteDrain / reschedule logic, unchanged ...
        cursor = (cursor + 1) % shardCount;
    }
}
```

## 4. Data structures, capacity, and correctness preservation

- **Depth tracking**: `ConcurrentQueue<T>.Count` in modern .NET is a cheap, non-locking read that
  sums a small number of segment counts (not the old O(n) walk, and not lock-acquiring) — used
  directly for the sweep's "is this queue worth trying" pre-check and for gating the idle-wake
  release, instead of hand-maintaining a parallel `Interlocked` counter array (as the shipped fix
  does for its `Channel`-based shards). One less piece of hand-rolled state to keep in sync.
  **Contingency**: if profiling shows `.Count` itself is a measurable hotspot, fall back to the
  `Interlocked` counter pattern already proven in the shipped fix — a small, self-contained change,
  not a redesign.
- **0→1 gating is best-effort, not exact**: `Count == 1` right after `Enqueue` can race with a
  concurrent dequeue on the same queue and either double-release or (rarely) under-release the
  semaphore. This is safe by construction, not just acceptable: an extra `Release()` just wakes an
  idle worker slightly early (it re-sweeps, possibly finds nothing, goes back to waiting — cheap); a
  missed `Release()` cannot strand work, because any worker that later goes idle re-sweeps every
  queue from scratch before waiting again, and will find the item. The same reasoning applied to the
  shipped fix's `Interlocked.Increment(...) == 1` gate, which is exact rather than best-effort;
  using `Count` trades that exactness for simplicity, on the same "never lose an item" foundation.
- **Bounded queue / backpressure** (`SchedulerReadyQueueCapacity`, `SchedulerOverloadMode`):
  `ConcurrentQueue<T>` has no native capacity limit, unlike `Channel<T>`. Gate capacity with a
  per-shard `SemaphoreSlim(capacity)` acquired before enqueue and released after a successful
  dequeue — **only when `SchedulerReadyQueueCapacity > 0`** (the non-default, opt-in case). The
  default unbounded path (what PingPong, AstroSim, and most workloads use) skips this construct
  entirely, so it costs nothing in the common case. `RejectWhenFull` uses a non-blocking
  `Wait(0)` try-acquire; `Wait` mode uses `WaitAsync(ct)`.
- **Shutdown**: simpler than the `Channel`-based version — no `Writer.TryComplete()` equivalent is
  needed since `ConcurrentQueue<T>` has no "no more writes" signal to give. `DisposeAsync` just
  cancels `_cts`; every worker's `_workSignal.WaitAsync(ct)` throws `OperationCanceledException`
  promptly and the sweep loop exits. Any activations still queued at that point are abandoned —
  identical best-effort semantics to today (the current design does not guarantee full drain before
  shutdown either).
- **Correctness invariants** (FIFO-per-activation ordering, single-in-flight-drain-per-activation,
  the N-way concurrency guarantee): unaffected by the collection swap. They're enforced by
  `GrainActivation`'s own per-activation mailbox channel (`SingleReader = true`, untouched by this
  change at all) and its `TryBeginDrain`/`CompleteDrain` CAS flag. The scheduler only ever decides
  which worker thread services a *ready* activation next — never the order of messages within that
  activation's own turn, and never whether more than one turn can run at once for it.

## 5. Testing plan

1. `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerTests.cs` (all 11 spec tests,
   including `Spec6`/`Spec7`'s concurrency-cap liveness guarantees) run repeatedly (5-10x) — these
   are exactly the tests that would catch a correctness regression from the collection swap.
2. Full `Quark.Tests.Unit` + `Quark.Tests.Integration` suites, multiple runs, cross-checked against
   `project_flaky_tests` memory so a real regression isn't masked by, or mistaken for, documented
   environmental flakiness.
3. **New**: a dedicated concurrent-producer stress test — many threads calling `ScheduleAsync`
   concurrently across a small worker count, asserting every scheduled activation is eventually
   drained exactly once (no lost activations, no two concurrent drains of the same activation). Not
   covered by the existing suite, which exercises correctness more than raw concurrent-load stress —
   the kind of test that would have caught [[project_mailbox_workitem_pool_race]] had it existed at
   the time.
4. PingPong re-benchmark (32 pairs, same methodology as the shipped fix) plus a fresh `dotnet-trace`
   sampled-thread-time profile, to confirm the contention story (`Monitor.Enter_Slowpath` share,
   parent-frame breakdown) actually improves this time, not just the throughput number.
5. AstroSim re-run at 1M-body scale for regression-shape coverage (real per-tick work between calls,
   different access pattern than PingPong's constant empty↔non-empty oscillation).

## 6. Non-goals

- Chase-Lev deque or any other hand-rolled lock-free structure — deliberately not attempted this
  pass; §2 explains why. Worth revisiting only if `ConcurrentQueue`-per-worker's ceiling turns out to
  be insufficient after honest measurement.
- Piggybacking on the CLR `ThreadPool` directly — Orleans' approach, lower risk/control trade-off,
  not pursued this pass (see §2, option 3).
- Any change to `GrainActivation`'s own mailbox channel, `TryBeginDrain`/`CompleteDrain`, drain
  budget/fairness-yield logic, or diagnostics event shapes — all orthogonal to this change and
  untouched.
- No numeric throughput target is committed to here. The shipped fix's own spec already documents
  that this exact prediction went wrong once (multiple-x hypothesized, +7% delivered); this design
  will be measured and reported honestly rather than pre-committing to a number.
