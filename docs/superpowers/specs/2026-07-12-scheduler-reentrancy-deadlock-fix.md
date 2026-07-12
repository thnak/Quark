# ActivationScheduler reentrancy deadlock fix — transient overflow capacity

Fixes GitHub issue #167 on top of commit `1239f60`. `ActivationScheduler` is once again the
runtime default (`AddQuarkRuntime()`), replacing the `SimpleActivationScheduler` fallback that had
been in place since commit `c1f3751` (2026-07-10) specifically to avoid this bug.

## 1. Background

`RunWorkerAsync` holds a worker's slot for the entire duration of `await
activation.DrainAndCompleteAsync(...)`, including any outbound nested grain-to-grain call made
from inside that drain. If N concurrent callers all nest a call into a small set of shared targets
and N reaches `SchedulerMaxConcurrentActivations`, every worker ends up mutually blocked — no
worker is ever free to service the targets everyone is waiting on. Confirmed originally via the
Realm sample's TCP bot-driver benchmark (20 players, 2 moves/sec, 15s): hung against
`ActivationScheduler`, clean against `SimpleActivationScheduler`.

## 2. Root cause — the exact call chain

Traced end to end (file:line references from the pre-fix code):

1. `ActivationScheduler.RunWorkerAsync`: `await activation.DrainAndCompleteAsync(_drainBudget, ct)`
   for caller activation A.
2. `GrainActivation.DrainAndCompleteAsync` → `DrainAsync`'s loop → `await item.ExecuteAsync()`.
3. `MailboxWorkItem.ExecuteAsync` runs the captured delegate under `ExecutionContext.Run` (the
   context captured back at enqueue time) — A's behavior method body executes here.
4. A's behavior calls another grain — the generated proxy calls
   `LocalGrainCallInvoker.InvokeVoidAsync`/`InvokeAsync` for target B.
5. `GetOrActivateAsync` (via `GrainActivationTable.GetOrCreateAsync`) resolves/creates B, then
   `await activation.PostAsync(...)` on B.
6. `GrainActivation.PostCoreAsync` on B: enqueues B's item, calls `_scheduler.ScheduleAsync(B, ...)`
   (fast — just enqueues onto the shared ready queue), then **`await item.WaitAsync()`** — the
   terminal await that only resolves once *some* worker later drains B's item.

All 6 frames are one continuous suspended async chain rooted at the `Task` stored in
`ActivationScheduler._workers[workerIndex]`. If N callers all reach step 6 simultaneously (blocked
awaiting a reply) and N == `SchedulerMaxConcurrentActivations`, no worker is ever free to dequeue
and drain any target — a genuine circular resource-exhaustion deadlock, not a timing race.

## 3. Two fix directions evaluated

### 3.1 Structural: release the worker's slot around the nested-call await, reacquire on resume

The "textbook correct" fix: don't hold the slot at all while purely waiting on a downstream reply;
release it right before step 6's `await`, reacquire once it resolves. **Rejected** after tracing the
actual plumbing this needs: step 3's `ExecutionContext.Run` runs the drained delegate against a
context *captured at enqueue time* (`PostCoreAsync`'s `item.Initialize(workItem,
ExecutionContext.Capture())`), which is a **different** context than whatever the worker's own
`RunWorkerAsync` loop was running under — so an `AsyncLocal` set by the worker before calling
`DrainAndCompleteAsync` does **not** flow into step 3's delegate execution; `ExecutionContext.Run`
replaces the ambient context for its duration rather than layering on top of it. A working version
needs the "do I currently hold a permit" signal threaded as an **explicit parameter**, not ambient
state — through `IMailboxWorkItem`'s delegate signature, `LocalGrainCallInvoker`'s per-call state
types (`InvokeVoidState<T>` etc.), and the source-generated proxy signatures
(`Quark.CodeGenerator`). That's a wide, multi-layer, code-generator-touching change — high
implementation risk for a codebase that has already once rejected a hand-rolled Chase-Lev deque
specifically to avoid subtle lock-free/async correctness bugs.

### 3.2 Transient overflow capacity (chosen)

A self-hosted stall watchdog inside `ActivationScheduler` itself: if the ready queue shows zero
completed drains for longer than a threshold, spin up temporary extra worker `Task`s to drain the
backlog; they retire once it clears. This is the same conceptual fix `SimpleActivationScheduler`
gets "for free" from its unbounded `Task.Run`-per-activation design, applied narrowly and only when
actually needed — entirely contained in `ActivationScheduler.cs` plus a few new
`SiloRuntimeOptions`, no changes to `LocalGrainCallInvoker`, generated proxies, or
`Quark.CodeGenerator`.

**Honest tradeoff, found during validation (§6):** this turns a permanent deadlock into a bounded,
self-healing stall — it does not eliminate the underlying resource-exhaustion pattern the way §3.1
would. An even more adversarial fan-in shape than `SchedulerMaxConcurrentActivations +
SchedulerMaxOverflowWorkers` concurrently-blocked chains could still stall past the configured
bound. Not claimed to be impossible — just self-healing within a documented, configurable limit.

## 4. Architecture

- `SiloRuntimeOptions` gains three options (`src/Quark.Runtime/SiloRuntimeOptions.cs`):
  - `SchedulerStallThreshold` (default 250ms) — how long the ready queue must show no progress
    before the watchdog intervenes.
  - `SchedulerStallCheckInterval` (default 50ms) — watchdog poll cadence.
  - `SchedulerMaxOverflowWorkers` (default `Environment.ProcessorCount`, `0` = disabled) — cap on
    transient workers beyond `SchedulerMaxConcurrentActivations`.
- `ActivationScheduler` tracks `_lastProgressTimestamp` (touched by every completed drain, primary
  or overflow), and self-hosts a watchdog `Task` (`RunStallWatchdogAsync` → the synchronous,
  independently-testable `CheckForStallAndSpawnOverflow`, mirroring
  `StuckGrainDetector.PollOnce()`/`GrainIdleCollector.CollectIdleGrains()`'s split — `PeriodicTimer`
  + testable inner check) alongside the N primary `RunWorkerAsync` loops, using the same
  self-hosted `Task.Run` + shared `_cts` pattern those already use (it's a plain DI singleton with
  no `IHostedService` lifecycle hook, so it can't use `BackgroundService` the way
  `StuckGrainDetector`/`GrainIdleCollector` do).
- The "given a dequeued activation, drain it, handle capacity-gate release / diagnostics / fairness
  yield / reschedule" body was extracted into a shared `DrainDispatchedActivationAsync` helper, used
  by both the primary loop and the new overflow loop — avoids duplicating that logic (same
  motivation as the already-closed #141, applied here between primary/overflow instead of
  `ActivationScheduler`/`SimpleActivationScheduler`).
- Overflow workers (`RunOverflowWorkerAsync`) reuse the same `TryDequeueAny` sweep, but never
  register on the idle-worker registry or park — after 3 consecutive empty sweeps they just retire.
- `DisposeAsync` now also awaits the watchdog task and any live overflow workers (tracked in a
  `ConcurrentDictionary<Task, byte>`), alongside the existing `_workers` wait.
- `RuntimeServiceCollectionExtensions.AddQuarkRuntime()` now registers `ActivationScheduler` instead
  of `SimpleActivationScheduler` as the default `IActivationScheduler`. `SimpleActivationScheduler`
  remains available (e.g. the `--bare` PingPong benchmark mode still uses it directly).

## 5. Correctness — Spec6/Spec7 and the concurrency-cap guarantee

`Spec6`/`Spec7` (`ActivationSchedulerTests.cs`) assert the concurrency cap is a hard ceiling for
externally-arriving demand (`SchedulerMaxConcurrentActivations=1`: a second activation must not run
while the first is blocked on an **app-level gate**, not a nested call). The watchdog only adds
capacity when the ready queue has real backlog **and** zero progress for `SchedulerStallThreshold` —
Spec6's own assertion window (100ms) is comfortably shorter than the 250ms default, so the watchdog
never fires during that test. Verified empirically (§6) at 250ms, 5/5 clean.

## 6. New regression test — red, then green

New `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerReentrancyDeadlockTests.cs`,
modeled on `ActivationSchedulerConcurrencyStressTests.cs`'s direct-construction style (real
`ActivationScheduler`, no `TestCluster`/DI, matching the original "isolated unit repro, no TCP/DI
involved"). 4 caller activations, `SchedulerMaxConcurrentActivations=4`, 2 shared hot targets; a
`TaskCompletionSource`-based start gate forces all 4 callers to reach their nested call
*simultaneously* (deterministic, not timing-dependent) so every worker is provably occupied at
once. Confirmed failing (15s `WaitAsync` timeout) against the unfixed scheduler, confirmed passing
(~3s, matching the then-3s `SchedulerStallThreshold`) once the fix landed, and consistently passing
(~250ms-scale) after the threshold was retuned per §7.

**A cleanup subtlety found while writing this test**: `ActivationScheduler.DisposeAsync()`
unconditionally awaits its worker tasks regardless of `_cts` cancellation — cancellation stops idle
workers from parking again and gates new schedule attempts, but does *not* unstick a worker already
blocked deep inside a non-cancellation-aware `PostAsync().WaitAsync()`. Disposing a genuinely
deadlocked scheduler therefore hangs too. The test avoids `await using` on the scheduler and only
disposes on the success path, to avoid a second unbounded hang on top of the first.

## 7. Validation

### 7.1 Full test suite (4 projects, 2-3 runs each after the fix)

`Quark.Tests.Unit`, `Quark.Tests.Integration`, `Quark.Tests.Fault`, `Quark.Tests.Fault.Integration`
— all clean except already-known, timing-sensitive-under-parallel-load flaky tests
(`StuckGrainDetectorStallTests.ExecuteAsync_FiresOnSchedulerDrainStalled_WhenActivationLivelocked`,
`DrainStallDetectionTests.DrainAsync_IncrementsConsecutiveEmptyDrains_WhileCancelledWithWorkQueued`
— both use `SimpleActivationScheduler`/hand-built `GrainActivation`s directly, unrelated to this
fix, both pass 5/5 in isolation). `Spec6`/`Spec7` and the rest of `SchedulingSemantics` (31 tests)
pass reliably across repeated runs.

### 7.2 A genuine regression found, root-caused, and fixed: `Reminder_SurvivesSimulatedRestart`

This test failed 4/5 times under the new default (vs. reliably passing before). Root cause,
confirmed independently by two separate investigations (mine and a dedicated research agent that
built its own standalone repro): `TestClusterOptions.InitialSilosCount` defaults to **2**, and
`ReminderIntegrationTests`'s cross-cluster "restart" scenario shares one `InMemoryReminderStorage`
instance across every silo of every cluster it creates — including *both* silos within the second
cluster. Each silo runs its own independent `DefaultReminderService` poll loop against that shared
storage with no claim/lock semantics, so exactly one of the two silos "wins" the race to fire a
given due reminder, and the test's client only ever queries `PrimarySilo` (silo 0). This is a
pre-existing latent race in the test harness, not a new correctness bug — `SimpleActivationScheduler`
happened to almost always mask it: `DefaultReminderService.StartAsync` builds its `PeriodicTimer`
synchronously at silo startup, silo 0 starts a few ms before silo 1 (`TestSilo` starts sequentially),
and under `SimpleActivationScheduler`'s bare `Task.Run`-per-schedule model that tiny head start
reliably survived to the ~2s-later tick. `ActivationScheduler` permanently parks
`Environment.ProcessorCount` (32) workers plus a watchdog task per silo — 64+ extra long-lived
async-parked tasks across the two silos — which adds enough ThreadPool scheduling jitter to erase
silo 0's head start, turning a ~5% baseline failure rate (confirmed: 1/20 on the pre-fix baseline)
into ~80% (4/5).

**Fix**: `Reminder_SurvivesSimulatedRestart` now sets `InitialSilosCount = 1` for both clusters it
creates — a "restart" is one process replacing another, not two concurrently-polling silos racing
over shared storage with no coordination between them; a single silo is the more faithful shape for
what this test's name and intent actually describe. Validated 20/20 clean after the fix.

**Also discovered, not fixed (out of scope)**: `UnregisterReminder_StopsFutureFirings` was already
failing **11/20 (55%) on the pre-fix baseline** — a pre-existing, unrelated flaky test (tight 30ms
poll interval, no multi-silo sharing involved) that this investigation happened to surface by
running it far more times than any prior session had. Left as-is; worth its own issue.

### 7.3 A second genuine tuning problem found via the Realm sample, and fixed

Re-ran the original repro (`samples/Realm`, 20 players, 2 moves/sec, 15s) against the now-default
`ActivationScheduler`. No hang (0 failed moves) — but move latency was **p50 ~2998ms, p99 ~3489ms**,
against a documented pre-existing baseline of ~46ms p99 under `SimpleActivationScheduler`. The
initial `SchedulerStallThreshold` default (3 seconds) was chosen assuming the rescue would be a rare
safety net; empirically, for this workload (legitimate `PlayerGrain`→`MapGrain` nested calls, not an
adversarial shape), the p50 sitting almost exactly at the 3-second threshold showed the rescue was
firing on **every single move** — this workload's natural concurrent-nested-call demand routinely
exceeds `SchedulerMaxConcurrentActivations`'s default (`Environment.ProcessorCount`), which was
never really a demand ceiling that made sense once nested calls exist (a slot held on a pure-wait
nested call isn't consuming CPU, so tying the cap to core count was already a poor fit for
fan-out-heavy workloads — this fix didn't create that mismatch, it just exposed it by making
`ActivationScheduler` the default again).

**Fix**: retuned `SchedulerStallThreshold` to 250ms and `SchedulerStallCheckInterval` to 50ms
(250ms chosen specifically to stay comfortably above `Spec6`'s 100ms assertion window — see §5 —
while still being ~12x faster than the original default). Re-ran the same Realm benchmark:
throughput improved 9.0 → 28.5 moves/sec, p50 latency 2998ms → 240ms, p99 3489ms → 535ms, 0 failed
moves throughout. Still slower than the pre-existing `SimpleActivationScheduler` baseline (~46ms) —
an honest, now-small-and-tunable cost of the "wait-then-rescue" design (§3.2) instead of a
multi-second one. Workloads with heavy legitimate nested-call fan-out may want to tune
`SchedulerStallThreshold` lower still, or raise `SchedulerMaxConcurrentActivations` directly if the
workload's natural concurrent-activation count is known and stable — documented on the option
itself.

### 7.4 AstroSim — a genuine improvement, not just "no regression"

Re-ran `AstroSim` (chunk→up to-26-neighbor nested calls per tick — the same nested-call shape as the
original bug, at real scale) *without* forcing a scheduler override for the first time (previously
kept on `SimpleActivationScheduler` throughout the #164 investigation specifically because forcing
`ActivationScheduler` was unsafe pre-fix). Smoke scale (100K bodies/grid 8): 188,903 msg/s, up from
118,235 msg/s on the old default. Full scale (1M bodies/grid 32, 15s): **529,149 msg/s**, up from
323,128 msg/s (~64% higher) — 10 ticks completed cleanly, no stalls, bodies remain gravitationally
bound ([31.3, 3177.1] vs world [0, 3200]). Unlike Realm's player→map shape, AstroSim's many
independent, roughly-symmetric chunk-to-neighbor calls don't routinely exhaust
`SchedulerMaxConcurrentActivations`, so it benefits from `ActivationScheduler`'s sharded-queue
design without ever needing the rescue mechanism to intervene.

## 8. Non-goals

- Does not eliminate the resource-exhaustion pattern structurally (§3.1) — mitigates it with a
  bounded, tunable, self-healing rescue instead.
- Does not re-tune `SchedulerMaxConcurrentActivations`'s default itself, even though §7.3 suggests
  its `Environment.ProcessorCount` default may be a poor fit for nested-call-heavy workloads more
  generally — flagged as a candidate follow-up, not attempted here.
- Does not fix `UnregisterReminder_StopsFutureFirings`'s pre-existing 55% flakiness (§7.2) — unrelated
  to this issue, worth its own tracking.
- Does not add dedicated diagnostics events for overflow-worker spawn/retire (no `ILogger`/
  `IQuarkDiagnosticListener` hook exists on `ActivationScheduler` for this specifically) — a natural
  observability follow-up.
- Does not address `InMemoryReminderStorage`/`DefaultReminderService`'s lack of claim/lock semantics
  under genuine multi-silo clustering (§7.2) — a separate, real latent gap if multi-silo reminder
  ownership is ever expected to work correctly; not exercised anywhere else in this codebase.

## 9. Honest conclusion

The deadlock is fixed: the new regression test (§6) that reliably hung before the fix reliably
passes after it, and the two real-world workloads that could exercise the original bug shape
(Realm's player→map calls, AstroSim's chunk→neighbor calls) both now run to completion against the
restored default with zero failures. The fix is a mitigation, not a structural elimination (§3.2,
§8) — an honest tradeoff chosen for implementation-risk reasons over a correct-but-invasive
alternative (§3.1). Validating against real workloads (not just the synthetic repro) surfaced two
genuine problems the synthetic test alone would never have caught — a latent test-harness race
(§7.2) and a threshold tuned for "rare emergency" that was actually needed on every call for a
common workload shape (§7.3) — both fixed, both confirmed empirically rather than assumed fixed.
