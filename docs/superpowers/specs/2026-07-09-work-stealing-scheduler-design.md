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
private readonly int[] _shardCounts; // exact per-shard depth counter, see §4
private readonly SemaphoreSlim _workSignal = new(0, int.MaxValue);

private int ShardFor(GrainActivation activation)
    => (activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _shards.Length;

public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken ct = default)
{
    if (!activation.TryMarkScheduled()) return ValueTask.CompletedTask;
    activation.SetSchedulerEnqueueTime();
    int shardIndex = ShardFor(activation);
    _shards[shardIndex].Enqueue(activation);
    int depth = Interlocked.Increment(ref _shardCounts[shardIndex]); // exact 0->1 transition, see §4
    if (depth == 1)
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

- **Depth tracking**: a per-shard `int[] _shardCounts`, maintained with `Interlocked.Increment`/
  `Decrement` alongside every `Enqueue`/successful `TryDequeue`, backs both the sweep's "is this
  queue worth trying" pre-check and the idle-wake gate — the same `Interlocked` counter pattern
  already proven in the shipped `Channel`-based fix. `ConcurrentQueue<T>.Count` was tried first (a
  cheap, non-locking read) but was rejected: see the correctness note below.
- **0→1 gating must be exact, not best-effort**: an earlier draft of this design gated the idle-wake
  `Release()` on `ConcurrentQueue<T>.Count == 1` right after `Enqueue`, reasoning that a missed
  release was safe because "any worker that later goes idle re-sweeps every queue from scratch
  before waiting again." That reasoning is **wrong**: if all N workers are already parked on
  `_workSignal.WaitAsync` when two producers concurrently enqueue onto the same shard, both can
  observe a post-`Enqueue` `Count != 1` (e.g. both see `Count == 2`) and both skip `Release()` — there
  is no other worker still running that will later go idle and re-sweep, because every worker is
  already parked. That strands the enqueued activations indefinitely: a genuine lost-wakeup /
  indefinite-hang bug, not just a latency blip. Code review caught this before it shipped. The fix
  is the `Interlocked.Increment(ref _shardCounts[shardIndex]) == 1` gate shown above: the return
  value of `Interlocked.Increment` is atomic and unique per call, so of any set of concurrent
  same-shard enqueuers, exactly one observes the 0→1 transition and releases exactly once — no
  interleaving can cause a double-miss the way `ConcurrentQueue<T>.Count` could. This is the same
  exact-transition mechanism the predecessor sharded-`Channel<T>` fix
  (`docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md`) used; it is restored
  here, not reintroduced as new complexity.
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

## 7. Measured outcome (implemented and profiled 2026-07-09)

**Correctness: clean.** Full `Quark.Tests.Unit` suite passed 3/3 runs (only failure: a pre-existing,
unrelated harness race in `StuckGrainDetectorStallTests` — root-caused this session, see
`project_flaky_tests` memory; it never touches `ActivationScheduler` at all, so it's out of scope
here). `Quark.Tests.Integration` passed clean. Both new concurrency stress tests
(`ManyConcurrentProducers_AcrossManyActivations_NoWorkIsLost`,
`TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost`) pass reliably against the
shipped code. `SchedulingSemantics` (all 11 spec tests, including the `Spec6`/`Spec7`
concurrency-liveness guarantees) had zero failures across every run this session, including the
review loop's own repeated verification passes. Along the way, code review caught and fixed a real
lost-wakeup regression (a `ConcurrentQueue<T>.Count == 1` wake-gate check is not an atomic
transition detector, unlike the `Interlocked.Increment(...) == 1` it replaced) before it shipped —
see the corrected §3/§4 text above.

**Throughput: inconclusive, and reported as such rather than rounded to a headline number.** This
session's benchmark runs landed on a heavily shared dev box mid-measurement (12 concurrent users;
`uptime` load average observed swinging from ~11 to ~40 on 32 cores within single-digit minutes).
Raw PingPong runs on this design ranged 220,000–649,301 msg/s across 7 trials — a wider spread than
any code change plausibly explains on its own. To control for this, a throwaway build of the prior
shipped commit (`07ecd51`, the sharded-`Channel<T>` fix) was benchmarked back-to-back against this
design under matched real-time conditions. Even paired same-window comparisons disagreed in
direction: one pairing favored this design by +31.7% (589,775 vs 447,709 msg/s), a later pairing
favored the prior design by -3.8% (603,059 vs 626,818 msg/s) — confirming machine noise this
session was large enough to flip the sign of a single comparison. Aggregating all trials: median
589,775 msg/s (this design, n=7) vs 447,709 msg/s (control, measured today, n=5), mean 533,195 vs
446,820 — both point positive, in the same rough range the shipped fix's own +7% lived in, but
neither the direction nor a specific percentage can be claimed with confidence from this session's
data. Both design's numbers are also below the original historical baselines (621,290 /
665,962 msg/s) recorded at presumably lower ambient load, which is itself informative: it's evidence
those historical numbers already depended on the machine being quieter than it was this session,
not a property of either scheduler design.

**dotnet-trace: a real, useful, non-throughput signal.** 58 threads, 870,684.7 ms total sampled
thread-time, `UNMANAGED_CODE_TIME` 86.4% of total (comparable to the shipped fix's own 85%
baseline — this ratio held steady across the redesign). Contended-lock-acquire attempts
(`Monitor.Enter_Slowpath` opens, summed from the parent-frame table — the analysis script's
`call_count.get('Monitor.Enter_Slowpath', 0)` line under-reports as 0 because that dict is keyed by
the fully-qualified frame name, not the bare method name; use the parent-table sum instead) totaled
**70,100** — between the original single-queue baseline (~75,300) and the shipped sharded-`Channel<T>`
fix (~67,900). On this specific noisy trace, that's roughly on par with the shipped fix, not a clear
further reduction.

The parent-frame breakdown is the actionable finding:

| Site | Share of contended-lock samples |
|---|---|
| `ActivationScheduler.RunWorkerAsync` state machine (idle-wait path) | 49.8% (34,901) |
| `SemaphoreSlim.Release` (`_workSignal`) | 42.1% (29,512) |
| DI container `ResolveService` | 6.5% (4,566) |
| `Channel<T>` `TryWrite` (per-activation mailbox, not the ready queue) | 1.1% (763) |
| DI scope disposal | 0.5% (354) |
| `ActivationScheduler.EnqueueToShard` | ~0% (4) |

`ConcurrentQueue<T>`-internal frames are effectively **absent** from this list — confirming the
collection swap did eliminate the specific overhead this design set out to remove (`Channel<T>`'s
async-waiter/continuation-registration machinery paid even on non-blocking dequeue attempts). But
contention didn't disappear, it **moved**: ~92% of remaining lock-acquire attempts now happen on
the single shared `_workSignal` `SemaphoreSlim` — either inside its own `WaitAsync` wait-queue
bookkeeping (the `RunWorkerAsync` row) or its `Release` path. Every worker across the whole silo
still contends on this one primitive for idle-wake coordination; sharding the ready-queue storage
did not shard the wake signal. This is architecturally the same shape of problem the shipped fix
hit (a single point every worker must synchronize through), just relocated from the collection to
the semaphore.

**AstroSim: no regression, as predicted.** 129,700 msg/s (this design) vs 134,941 msg/s (a
same-conditions rebuild of `07ecd51`, benchmarked back-to-back) — within ~4%, i.e. noise, not a
code-driven difference. Both are well below the 206,539 msg/s recorded earlier this session at
(evidently) lower ambient load, consistent with the PingPong finding that today's absolute numbers
are suppressed by machine contention across the board, not by either scheduler design. This matches
the design's own expectation (§6) that AstroSim's real per-tick-work pattern wouldn't meaningfully
move either way.

**Honest bottom line.** This round did not clear, or fail to clear, the shipped fix's +7% ceiling —
the measurement environment this session was too noisy to responsibly claim a specific number in
either direction, and that itself is being reported plainly rather than picking the more flattering
of two disagreeing paired comparisons. What the trace evidence does show with more confidence
(percentages are more robust to absolute load noise than wall-clock throughput): the
`ConcurrentQueue<T>` swap worked exactly as designed at removing `Channel<T>`'s internal overhead
from the picture, but the silo-wide shared `_workSignal` semaphore is now the dominant remaining
contention point (~92% of contended-lock samples), not the ready-queue collection. A further
throughput push would need to target that specifically — e.g. sharding `_workSignal` itself (one
semaphore per worker, or per small worker group, rather than one for all N), or moving wake
coordination off `SemaphoreSlim` entirely toward the CLR-`ThreadPool`-piggyback approach (§2,
option 3) that was deliberately not attempted this pass. Re-measuring on a quieter box (or with a
controlled/reserved benchmark host) before drawing further throughput conclusions is the concrete
next step, not a rewrite of this design.
