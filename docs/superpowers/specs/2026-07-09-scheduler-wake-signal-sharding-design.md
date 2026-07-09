# Scheduler wake-signal sharding design

## 1. Background

The work-stealing `ConcurrentQueue`-per-shard redesign
(`docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md`, commit `7aa7ebc`, merged to
`main` 2026-07-09) shipped with clean correctness but inconclusive throughput, due to heavy shared-box
load noise during measurement. Its own "Measured outcome" section identifies the actionable finding
regardless of the noise: a fresh `dotnet-trace` parent-frame breakdown shows `ConcurrentQueue<T>`
internals are now effectively absent from contended-lock samples (the collection swap worked as
designed), but ~92% of remaining `Monitor.Enter_Slowpath` opens are now split between
`ActivationScheduler.RunWorkerAsync`'s idle-wait path (49.8%) and `SemaphoreSlim.Release` on
`_workSignal` (42.1%). Every worker across the whole silo still contends on one shared
`SemaphoreSlim(0, int.MaxValue)` for idle-wake coordination — sharding the ready-queue storage did not
shard the wake signal. This design targets that specifically.

## 2. Decision: per-worker semaphores + a lock-free idle-worker registry

Two simpler alternatives were considered and rejected in favor of a targeted-wake design:

1. **Grouped semaphores** (K semaphores, K ≈ √N, workers statically assigned round-robin) — lowest
   risk (structurally the same exact-transition-gate pattern repeated K times, no new correctness
   surface), but only a partial, bounded contention reduction (~K-fold at best; a group of N/K workers
   can still contend with each other). Rejected as the primary approach because it doesn't fully attack
   the measured bottleneck, though it remains a fallback if the chosen design doesn't clear the bar (see
   §6).
2. **Broadcast to all N per-worker semaphores** — trivially correct (every idle worker learns of every
   transition), but trades one contended object for N sequential `Release()` calls plus thundering-herd
   spurious wakeups on every transition. Rejected outright: likely to make matters worse at PingPong's
   transition rate, not better.
3. **Per-worker semaphores + idle-worker-targeted wake** (chosen) — one `SemaphoreSlim` per worker plus
   a lock-free `ConcurrentStack<int>` tracking which workers currently believe they're idle. On a
   shard's empty→non-empty transition, pop one idle worker's index and release only that worker's
   semaphore — exactly one `Release()` call, almost always uncontended since a private semaphore
   typically has 0-1 waiters. Highest contention-reduction ceiling of the three (approaches O(1) per
   transition regardless of N), at the cost of a new correctness surface (the idle-registry
   double-check) that must be reasoned through carefully — see §4.

## 3. Architecture

New fields alongside the existing `_shards`/`_shardCounts` in `ActivationScheduler`, sized by
`concurrency` (unchanged: `SchedulerMaxConcurrentActivations`):

```csharp
private readonly SemaphoreSlim[] _workerSignals;         // one per worker, SemaphoreSlim(0, int.MaxValue)
private readonly ConcurrentStack<int> _idleWorkers = new();  // indices of workers that believe they're idle
```

Everything else — `_shards`, `_shardCounts`, `_capacityGates`, `ShardFor`, the sweep-every-shard loop
body, `TryBeginDrain`/`DrainAsync`/`CompleteDrain` handling, diagnostics — is untouched. This design
only replaces the wake mechanism, not the queue, backpressure, or drain logic.

**Enqueue side** (`EnqueueToShard`): the existing exact-transition gate
(`Interlocked.Increment(ref _shardCounts[shardIndex]) == 1`) is unchanged. Only the action taken on that
gate changes:

```csharp
private void EnqueueToShard(int shardIndex, GrainActivation activation)
{
    ConcurrentQueue<GrainActivation> shard = _shards[shardIndex];
    shard.Enqueue(activation);
    int depth = Interlocked.Increment(ref _shardCounts[shardIndex]);
    if (depth == 1 && _idleWorkers.TryPop(out int idleWorker))
        _workerSignals[idleWorker].Release();
    // ... existing diagnostics/metrics, unchanged ...
}
```

If `TryPop` fails (no worker currently registered idle), do nothing. This is not a new liability: the
current shared-semaphore design already only guarantees *some* worker learns of a transition (a
`Release()` against a semaphore whose count is already > 0 doesn't create a second queued waiter) — a
busy worker will reach this shard on its own next full sweep after finishing whatever it's draining now,
exactly as today.

**Worker side** (`RunWorkerAsync`): the full-sweep loop body is factored into a small shared helper
(`TryDequeueAny(ref cursor)`, returning the found activation or `null` and advancing `cursor`) so the
main sweep and the double-check below don't duplicate the loop:

```csharp
private async Task RunWorkerAsync(int workerIndex, CancellationToken ct)
{
    int shardCount = _shards.Length;
    int cursor = workerIndex; // staggered start, same as today

    while (true)
    {
        GrainActivation? activation = TryDequeueAny(ref cursor);

        if (activation is null)
        {
            if (ct.IsCancellationRequested) return;

            _idleWorkers.Push(workerIndex);

            // Double-check: a transition may have landed between the sweep above finding
            // nothing and this push becoming visible to enqueuers. Re-sweep once before
            // parking. If this finds work, the idle-stack entry is deliberately left in
            // place rather than removed -- see §4.
            activation = TryDequeueAny(ref cursor);

            if (activation is null)
            {
                try { await _workerSignals[workerIndex].WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
        }

        // ... existing TryBeginDrain / DrainAsync / CompleteDrain / reschedule logic, unchanged ...
    }
}
```

`workerIndex` replaces today's `homeShard` parameter name — they're the same value (shard count == worker
count, unchanged), renamed here because it's now also the index into `_workerSignals`/`_idleWorkers`.

**Shutdown**: `DisposeAsync` disposes `_workerSignals[i]` for all `i` instead of the single `_workSignal`.
Cancellation behavior is unchanged: `_cts.Cancel()` causes every worker's `WaitAsync(ct)` to throw
`OperationCanceledException` promptly, same as today.

## 4. Correctness: the idle-registry double-check, and why no self-removal

**The double-check closes the classic lost-wakeup race by construction.** Push-then-recheck ordering
means: any transition landing before the worker's push is visible to that worker's own re-sweep (the
`Interlocked.Increment` the enqueuer performs happens-before the `TryPop` it might do, and the worker's
`Push` happens-before its own re-check's reads); any transition landing after the push is visible to the
enqueuer's `TryPop`, which will find the worker's index and release its specific semaphore, which the
subsequent `WaitAsync` will then consume without blocking (the semaphore's count is already positive).
There is no gap between "registered as idle" and "no longer capable of being notified."

**Why the design deliberately does not attempt self-removal on the "found work in the double-check"
path.** `ConcurrentStack<int>` has no "remove this specific value" operation — only `Pop`, which removes
whatever is currently on top. If a worker tried to cancel its own idle registration after finding work
via the re-check by popping the stack, it could pop a *different* worker's entry instead (one that
pushed later and landed on top), silently stranding that other worker's registration with no one left to
release its semaphore — a genuine stall, strictly worse than the contention problem this design exists to
fix. The chosen design instead accepts **stale idle-stack entries**: a worker can be listed as idle while
actually busy draining, and a targeted release can land on it as a wasted credit. This is safe:

- **No work is ever lost.** Every worker still full-sweeps every shard on every loop iteration,
  unconditionally, regardless of wake-signal state — identical to today's design. A stale-entry "wasted"
  release only means one particular transition didn't get its own dedicated wake; some worker (the one
  that's momentarily busy, once it frees up, or a different worker that goes idle later) will still reach
  that shard on its own next full sweep.
- **Staleness is self-correcting.** A leftover semaphore credit just makes that worker's *next* parking
  attempt a non-blocking no-op — it loops through one extra sweep pass instead of actually parking, then
  proceeds normally. It does not accumulate unboundedly: at most one stale credit can exist per idle-stack
  push, since a worker only pushes once per idle period.
- **Bounded delay, same class as today.** The current shared-`_workSignal` design already only guarantees
  "at least one worker becomes aware" of any given transition, not "every transition gets a dedicated
  wake" (a `Release()` on an already-positive semaphore doesn't queue a second waiter). This design does
  not regress that guarantee — it redistributes where the signal lands, with the same worst-case bound
  (one additional full sweep by whichever worker frees up next).

**Guarantees preserved, and why each is orthogonal to this change:**

| Guarantee | Why unaffected |
|---|---|
| N-way concurrency (`Spec6`/`Spec7`) | Every worker still sweeps every shard unconditionally; the wake mechanism only affects who gets nudged out of parking, never who is *allowed* to service a given activation. |
| FIFO-per-activation mailbox ordering | Enforced entirely inside `GrainActivation`'s own mailbox channel, untouched by this change. |
| Bounded-queue overload rejection (`_capacityGates`) | Independent per-shard `SemaphoreSlim` construct, unrelated to the wake path. |
| Drain-budget fairness yield | Happens inside `DrainAsync`/`CompleteDrain`, unrelated to the wake path. |
| Graceful shutdown / stuck-drain diagnostics | `DisposeAsync` cancellation semantics unchanged; only the number of semaphores disposed changes. |

## 5. Testing plan

1. **Existing regression gates, unmodified:** `ActivationSchedulerTests.cs` (all 11 spec tests,
   especially `Spec6`/`Spec7`) and both tests in `ActivationSchedulerConcurrencyStressTests.cs` — all
   still valid, since queue/shard semantics are untouched; only the wake path changed.
2. **New targeted test** for the wake path itself: same style as the `workerCount=1`/single-burst test
   that caught the prior lost-wakeup race
   (`TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost`). `workerCount=1` makes the
   idle-stack trivially either empty or contain exactly that one worker, exercising the full
   push→re-check→park sequence on every idle transition. Repeat ~300 iterations with a tight timeout,
   asserting exact drain counts, no lost/duplicated drains. Honest framing for the spec and any future
   reviewer: the push-then-recheck ordering closes the classic race *by construction* (§4) — this test's
   job is to guard against an implementation mistake (wrong ordering, wrong volatile/interlocked
   semantics), not to prove a new theorem the way the prior round's test did.
3. Full `Quark.Tests.Unit` + `Quark.Tests.Integration` suites, 3 runs, cross-checked against
   `project_flaky_tests` memory before treating any failure as a regression.

## 6. Measurement plan and success criteria

Given last round's paired PingPong comparisons disagreed in sign on this shared box, this round adds
measurement rigor rather than trusting a single comparison:

- `taskset`-pin both this design's build and a same-commit-family control build (throwaway worktree at
  `main` tip `7aa7ebc`) to a fixed, disjoint core set, isolating from other users' load as far as this
  shared box allows.
- 10+ paired PingPong trials per side, interleaved (not all-of-one-side-then-the-other), reporting median
  and spread rather than a single headline number.
- Re-run `dotnet-trace` + the parent-frame analysis on the new build: confirm whether
  `Monitor.Enter_Slowpath` sample count drops below the current ~70,100 baseline, and explicitly check
  for `ConcurrentStack` frames in the parent-of-monitor breakdown — the idle-worker registry is a new
  shared structure; its lock-freedom should be confirmed in the trace, not assumed.
- AstroSim regression check at the same 1M-body scale — not a target for improvement, a check that
  nothing broke.

**Success criteria for this round:** ship only if (a) the CPU-pinned paired trials show a consistent,
non-sign-flipping improvement over the control, (b) `Monitor.Enter_Slowpath` count measurably drops, and
(c) all correctness tests are clean. If the throughput comparison remains ambiguous even CPU-pinned,
report that plainly rather than rounding up — same standard as the prior two rounds this session
(`docs/superpowers/specs/2026-07-08-scheduler-ready-queue-contention-fix.md`,
`docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md`).

## 7. Non-goals

- Grouped/K-way semaphores (§2, option 1) — not attempted this pass; the design goes straight to the
  more aggressive targeted-wake approach per explicit user direction. Worth revisiting as a
  lower-complexity fallback if the idle-registry design's correctness or measured results don't clear
  the bar.
- Any change to `GrainActivation`'s own mailbox channel, `TryBeginDrain`/`CompleteDrain`, drain
  budget/fairness-yield logic, `_shards`/`_shardCounts`/`ShardFor`, or `_capacityGates` — all orthogonal
  to this change and untouched.
- A new diagnostic/metric distinguishing "idle-stack pop hit" from "pop miss" (i.e., how often a
  transition finds no idle worker to target) — potentially useful for future tuning, but out of scope
  for this round; can be added later without touching the wake algorithm itself.
- Piggybacking on the CLR `ThreadPool`'s own work-stealing (the Orleans approach, considered and
  deferred in the prior spec's §2 option 3) — still not pursued here.
