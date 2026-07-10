# Design: Runtime-quality benchmark suite (contention, fairness, scheduling, lifecycle, backpressure)

**Date:** 2026-07-10
**Status:** Implemented
**Lives in:** `tests/Quark.Performance/` (existing benchmark project)

## 1. Goal

The existing benchmark project answers "how fast is one grain call" (`GrainCallBenchmarks`,
`DispatchPipelineBenchmarks`) and "what sustained throughput can the cluster hit" (`PingPong`,
`AstroSim`). It does not answer eight runtime-quality questions that only show up under
*concurrent, heterogeneous, or adversarial* load: contention across many independent mailboxes,
scheduler fairness when one grain is hammered, scheduling quality (ready-queue wait time), actor
creation/destruction cost, allocation behavior under contention, cache-locality effects (false
sharing, scheduler shard-hash imbalance), backpressure behavior at the mailbox/scheduler layer, and
latency tail (p99/p999). This spec adds five new standalone runners, two new BenchmarkDotNet
suites, and a small `Shared/` utility library covering all eight in one combined design, since they
share the same `LatencyHistogram`/`IWorkGrain` machinery and would otherwise repeat that narrative
eight times.

## 2. Non-goals

- No new NuGet dependency for percentile tracking — a hand-rolled `LatencyHistogram` (per-worker
  buffers, merged + sorted once) replaces what a library like HdrHistogram would provide, matching
  the project's "explicit registration, no scanning" ethos and avoiding an AOT/trim-risk vet for a
  test-only project.
- No built-in parameter-sweep automation — matches the existing single-shot-per-invocation
  convention (`PingPong`/`AstroSim` are re-run manually at different CLI args, not looped
  internally).
- Multi-silo / TCP. Same in-process-only rationale as PingPong/AstroSim/DispatchPipeline.
- Fixing anything the new benchmarks reveal. This is measurement infrastructure; any regression or
  tuning opportunity it surfaces gets its own later spec.

## 3. Shared utilities (`tests/Quark.Performance/Shared/`)

- **`LatencyHistogram`** — `ThreadLocal<List<double>>` per-worker sample buffers (so concurrent
  `Record()` calls never contend with each other, generalizing the `PaddedCounter` "no shared
  mutable hot path" lesson from a running sum to a growable list). `Merge()` combines every
  thread's buffer, sorts once, and computes `Percentiles(Count, Mean, P50, P90, P99, P999, Max)` by
  nearest-rank. All new runners record consistently in microseconds; ms-based diagnostic events
  (`SchedulerActivationWaitedEvent.WaitMs`, `SchedulerDrainCompletedEvent.DurationMs`) are ×1000'd
  before recording.
- **`PaddedCounter`** — cache-line-padded (`[StructLayout(LayoutKind.Explicit, Size = 64)]`)
  per-worker counter, made `public` for reuse. Generalizes the private struct already used by
  `PingPong/PingPongRunner.cs`; that file is left untouched (its own private copy stays as-is —
  not worth touching working, heavily-commented code for a rename).
- **`WorkSimulator.BusySpinMicroseconds(int)`** — busy-spins via `Stopwatch`, deliberately not
  `Task.Delay`. A drain worker awaiting `Task.Delay` yields its thread back to the pool mid-drain,
  understating the cost of real CPU-bound grain work actually occupying a scheduler worker — the
  fairness, scheduling-quality, and backpressure runners all depend on this being held constant.
  `Task.Delay`'s coarse timer granularity (~1ms+ on Linux, ~15ms on Windows) also can't represent
  the tens-of-microseconds range `--work-us`/`--*-work-us` vary over.
- **`IWorkGrain`/`WorkGrainBehavior`/`WorkGrainBehavior_DoWorkInvokable`/`WorkGrainProxy`** — one
  shared hand-wired grain (`DoWorkAsync(int microseconds) -> ValueTask<long>`, busy-spins then
  returns an incrementing per-activation call count) reused by `MailboxContention`, `Fairness`,
  `SchedulingQuality`, `Backpressure`, and `AllocationBenchmarks` instead of five near-duplicate
  grain types.

## 4. Mailbox contention (`MailboxContention/MailboxContentionRunner.cs`)

Varies grain count (`--grains`, parallelism across independent, serialized mailboxes) and
callers-per-grain (`--callers-per-grain`, contention on one grain's single serialized mailbox)
independently. `TestCluster` with `--grains` `IWorkGrain`s, `grains × callersPerGrain` tight-loop
workers each timing its own call into a shared `LatencyHistogram`, plus a 1s progress reporter
matching `PingPongRunner`'s cumulative/instantaneous style. Reports calls/s and latency percentiles
— increasing `--callers-per-grain` shows contention cost on one mailbox; increasing `--grains`
shows independent-mailbox parallelism scaling.

## 5. Fairness (`Fairness/FairnessRunner.cs` + `FairnessDiagnosticListener.cs`)

One "hot" grain hammered continuously by `--hot-callers` concurrent callers (each call doing
`--hot-work-us` of busy-spin work) alongside `--cold-grains` lightly-called grains
(`--cold-work-us`/`--cold-call-interval-ms`). Two phases: `--baseline-seconds` with only cold
grains running (establishes `coldBaselineHistogram`), then `--duration` with the hot grain also
active (`coldWithHotHistogram`). The delta between the two cold-grain latency distributions is the
starvation cost `SiloRuntimeOptions.SchedulerDrainBudget`'s yield mechanism is meant to bound.
`--drain-budget` maps directly to that option; `FairnessDiagnosticListener` counts
`OnSchedulerDrainYielded` events (registered as a direct `AddSingleton<IQuarkDiagnosticListener>`
instance — **not** `services.AddQuarkDiagnostics(listener)`, which has a documented circular-DI bug,
see `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md` §5). Re-running at different
`--drain-budget` values shows the fairness/throughput tradeoff directly.

## 6. Scheduling quality (`SchedulingQuality/SchedulingQualityRunner.cs` + `SchedulingQualityDiagnosticListener.cs`)

Sets `SiloRuntimeOptions.SchedulerMaxConcurrentActivations` from `--scheduler-workers`. A single
dispatcher loop round-robins `--activations` grains, firing each call via `Task.Run(...)` **without
awaiting it to completion** before the next dispatch (`await Task.Delay(dispatchIntervalMs)`
between dispatches instead) — this keeps calls in strict round-robin dispatch order while many stay
concurrently in flight, which is what actually exercises `--scheduler-workers` concurrency; a
purely sequential await-then-dispatch loop never would. `SchedulingQualityDiagnosticListener` feeds
`OnSchedulerActivationWaited` (ready-queue wait time) and `OnSchedulerDrainCompleted` (drain
duration + items processed) into two `LatencyHistogram`s. Reports both distributions plus average
items/drain.

## 7. Actor creation/destruction (`ActorLifecycle/`)

Bypasses `TestCluster`/proxies entirely — a raw `ServiceCollection` + `AddQuarkRuntime()` +
`GrainTypeRegistry.Register(...)`, same pattern as `DispatchPipelineBenchmarks.SetupAsync` — so a
unique `GrainId` every iteration forces a genuinely fresh activation through
`GrainActivationTable.GetOrCreateAsync` rather than reusing a warm one. `IActorLifecycleGrain`
implements `IActivationLifecycle` so real `OnActivateAsync`/`OnDeactivateAsync` hooks fire.
Destruction goes through `GrainActivationTable.TryDeactivateAsync` — previously dead code (marked
`// TODO did not called anywhere` in `GrainActivationTable.cs`), now load-bearing: it awaits a real
`GrainActivation.DisposeAsync()`. Reports creations/sec, destructions/sec, and create/destroy
latency percentiles across `--parallelism` concurrent workers.

**`--allocations` correctness finding.** `GrainActivation`'s mailbox completion signal is always
forced onto the thread pool (`RunContinuationsAsynchronously = true`,
`GrainActivation.cs:959`, deliberately, to avoid a nested-drain deadlock) — so the calling thread
and the thread that actually runs `OnActivateAsync`/`RunDeactivationAsync` are frequently
different. A thread-local `GC.GetAllocatedBytesForCurrentThread()` delta would only capture the
caller-side invoker/DI-scope allocations, missing the grain-side activation/deactivation work (and
could even go negative if the resuming continuation lands on a thread with a lower running total).
`--allocations` instead uses `GC.GetTotalAllocatedBytes(precise: true)` (process-wide) around each
full create+call+destroy round trip, and forces `--parallelism 1` so only one round trip is ever
in flight — a process-wide counter correctly attributes every allocation from that round trip
regardless of which thread(s) it touched. **Known caveat:** background scheduler-worker threads
(one per `Environment.ProcessorCount` by default, always running even when idle-parked on a
semaphore) and GC bookkeeping still add some noise between the before/after snapshots — mitigation
is statistical (read the median across many iterations), not a hard guarantee.

## 8. Backpressure (`Backpressure/BackpressureRunner.cs`)

`--scope mailbox`: one target `IWorkGrain`, `--callers` concurrent callers with no think time,
`SiloRuntimeOptions.MailboxCapacity`/`MailboxFullMode` from CLI. `--scope scheduler`: `--grains`
distinct grains each with one caller loop, `SchedulerReadyQueueCapacity`/`SchedulerOverloadMode`
from CLI. `--work-us` defaults to `500` (deliberately expensive) so the queue actually backs up.

**Scheduler-scope correctness finding.** `ActivationScheduler.ScheduleAsync` calls
`TryMarkScheduled()` first and returns immediately if the activation is already scheduled/draining
— only an idle→scheduled transition passes through the ready-queue capacity gate
(`ActivationScheduler.cs:128`). This means **a single hot grain can never trigger
`SchedulerOverloadException` by itself**: once it's continuously scheduled, every subsequent
`PostAsync` bypasses the gate entirely. Scheduler-scope backpressure only bites with many distinct,
mostly-idle grains repeatedly transitioning idle→scheduled — hence `--scope scheduler` uses
`--grains` grains with one caller loop each, not one grain with many callers. A future reader
"fixing" this back to a single hot grain would silently stop observing any scheduler-level
rejections or waits.

Under `Wait` mode (either scope), each call's own await duration *is* the backpressure signal
(recorded into a `LatencyHistogram` — the mailbox write or scheduler admission genuinely blocks
inside that await). Under `RejectWhenFull`, `MailboxFullException`/`SchedulerOverloadException`
are caught and counted (accepted vs. rejected, via `PaddedCounter`s) instead.

## 9. Memory allocation (`AllocationBenchmarks.cs`, BenchmarkDotNet suite)

`[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]`, `[MemoryDiagnoser]`, sync
`Setup()`/`Cleanup()` wrappers blocking on async internally (BenchmarkDotNet's `InProcessEmit`
toolchain does not reliably await a `Task`-returning `[GlobalSetup]`/`[GlobalCleanup]` — same
documented gotcha as `DispatchPipelineBenchmarks`). Reuses `Shared.IWorkGrain`/`WorkGrainBehavior`
against a hand-built `ServiceProvider` + direct `IGrainCallInvoker` calls — no `TestCluster`, no
proxy. Three benchmarks: `SingleGrainSequential` (baseline, `microseconds: 0` to isolate allocation
from spin time — should roughly track `DispatchPipelineBenchmarks.FullInvokeVoidAsync`'s profile,
though not identically, since this exercises the typed-result `MailboxWorkItem<TState,TResult>`
path rather than the void one), `SingleGrainConcurrentContention` (8 concurrent callers via
`Task.WhenAll` against the *same* grain — isolates same-mailbox contention overhead), and
`NGrainFanOut` (8 concurrent callers against 8 *distinct* grains — isolates fan-out cost from
same-grain contention). BenchmarkDotNet's `[MemoryDiagnoser]` hooks process-level GC counters
around each iteration, so — unlike a hand-rolled thread-local counter — it doesn't hit the
cross-thread-attribution problem from §7; this is the right tool for allocation-under-concurrency.
Read the contention/fan-out results as directional (contention adds allocation vs. baseline), not
exact deltas — BenchmarkDotNet's own docs note some imprecision measuring allocations across
multiple threads within one iteration.

## 9a. Activation/deactivation allocation (`ActivationLifecycleBenchmarks.cs`, added 2026-07-10 follow-up)

Precise, `[MemoryDiagnoser]`-measured counterpart to `ActorLifecycle --allocations`'s hand-rolled
`GC.GetTotalAllocatedBytes` check (§7's caveat about cross-thread attribution noise doesn't apply
here — BenchmarkDotNet hooks process-level GC counters, not a thread-local delta). Reuses
`ActorLifecycle`'s no-op `IActivationLifecycle` grain (`IActorLifecycleGrain`/
`ActorLifecycleGrainBehavior`) against a hand-built `ServiceProvider` + direct
`IGrainCallInvoker`/`GrainActivationTable` calls, same pattern as `DispatchPipelineBenchmarks`.
Three benchmarks: `GrainActivate` (fresh `GrainId` every invocation, isolates activation cost
alone — deliberately left activated, torn down in bulk by `GlobalCleanup`), `GrainDeactivate`
(isolates deactivation cost alone via `[IterationSetup(Target = nameof(GrainDeactivate))]`, which
pre-activates a fresh grain immediately before each measured call), and
`GrainActivateDeactivateRoundTrip` (the real end-to-end cost).

**`GrainDeactivate`'s correctness depends on exactly one measured invocation per iteration** — if
BenchmarkDotNet's pilot stage batches many invocations into one iteration (its default `Throughput`
strategy calibrates toward this for cheap operations), only the *first* invocation in a batch
would see a validly pre-activated grain; every subsequent one in that same batch would silently
no-op against an already-removed `GrainId` (`GrainActivationTable.TryDeactivateAsync` returns
immediately on a miss — no exception, just a corrupted, artificially-fast measurement). The class
declares `[SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 3, iterationCount: 10)]` to
force one invocation per iteration.

**Invocation caveat confirmed by validation.** BenchmarkDotNet's CLI *unions* any `--strategy`/
`--job`/`--inProcess` argument with the class's own attribute-level job rather than overriding it —
running `--filter '*ActivationLifecycleBenchmarks*' --inProcess --strategy ColdStart` therefore
produces **two** job rows per benchmark: the attribute's own out-of-process job (which reports `NA`
across the board here — a pre-existing, already-documented issue, "stale locked worktree
ambiguates project discovery," identical to the gotcha already on `DispatchPipelineBenchmarks`) and
a second, `Toolchain=InProcessEmitToolchain` row from the CLI override, which is the real result.
This is not new/specific to this suite — it's `DispatchPipelineBenchmarks`'s own documented
workflow ("Ignore the `Job-XXXXX`/`Toolchain=Default` row... the row with
`Toolchain=InProcessEmitToolchain` is the real result"), re-confirmed here. The README's
`DispatchPipelineBenchmarks` gotcha previously named the wrong flag (`--job ColdStart`, which this
BenchmarkDotNet version rejects with "invalid base job" — `--job` only accepts named presets
`default|dry|short|medium|long|verylong`); corrected to `--strategy ColdStart` (the flag that
actually controls `RunStrategy`).

**Validated results (2026-07-10, `--filter '*ActivationLifecycleBenchmarks*' --inProcess --strategy
ColdStart`, `InProcessEmitToolchain` row):**

| Method | Mean | Allocated |
|---|---:|---:|
| `GrainActivate` | 702.1 us | 12,208 B |
| `GrainDeactivate` (`InvocationCount=1`, confirming the per-invocation isolation held) | 588.3 us | ~0 B |
| `GrainActivateDeactivateRoundTrip` | 706.3 us | 14,128 B |

Deactivation allocates essentially nothing (mostly bookkeeping over already-allocated state);
activation's ~12KB accounts for nearly all of the round trip's ~14KB, consistent with activation
(DI scope creation, `OnActivateAsync` hook, table/directory registration) being the more
allocation-heavy half of the lifecycle.

## 10. Cache locality (`CacheLocalityBenchmarks.cs`, two BenchmarkDotNet classes in one file)

Two independent classes (not one, to avoid BenchmarkDotNet applying an irrelevant `[Params]` cross
product — e.g. re-running the false-sharing benchmarks once per `GrainCount`/`ShardCount`
combination would be meaningless):

- **`CacheLocalityBenchmarks`** — `[Params(2, 4, 8)] ThreadCount`, `UnpaddedConcurrentIncrement`
  (baseline, `Parallel.For` incrementing a shared unpadded `long[]`) vs.
  `PaddedConcurrentIncrement` (same but `Shared.PaddedCounter[]`) — generalizes the `PaddedCounter`
  lesson already present as a code comment in `PingPongRunner.cs` into a directly measured
  wall-clock number. Both variants allocate ~nothing, so the interesting column is Mean/Median
  time, not Allocated.
- **`SchedulerShardDistributionBenchmarks`** — replicates `ActivationScheduler.ShardFor`'s
  documented formula (`(hash & 0x7FFFFFFF) % shardCount`) as a local pure function, since the real
  method is `private`; a comment cross-references `ActivationScheduler.cs` so a future change to
  that formula prompts a look here too (this copy will otherwise silently drift). Shard imbalance
  is a *distributional* property, not a timing, so it can't be surfaced through BenchmarkDotNet's
  Mean/Allocated columns — it's computed once per `[Params(1000, 10000, 100000)] GrainCount` ×
  `[Params(4, 8, 16)] ShardCount` combination inside `[GlobalSetup]` and printed directly (max
  shard depth / mean / imbalance ratio), a visible side effect in BenchmarkDotNet's captured
  output. The `ShardHashComputation` benchmark itself times the legitimate throughput question:
  how cheap the scheduler's per-reschedule shard recomputation is.

## 11. Tail latency (p99/p999) — folded in, not a sixth runner

No standalone `TailLatency` tool. `LatencyHistogram` (§3) is the single shared mechanism, already
wired into `MailboxContention` (per-call latency), `Fairness` (cold-grain latency, baseline vs.
with-hot), `SchedulingQuality` (ready-queue wait + drain duration), `Backpressure` (`Wait`-mode
caller-side latency), and `ActorLifecycle` (create/destroy latency, and bytes/op under
`--allocations`) — each prints its own p50/p90/p99/p999/max on completion.
`AllocationBenchmarks`/`CacheLocalityBenchmarks` don't use it — they report BenchmarkDotNet's own
Mean/Error/StdDev/Allocated statistics, the already-standard convention for that half of the
project.

## 11a. Core scalability (`CoreScalability/CoreScalabilityRunner.cs`, added 2026-07-10 follow-up)

Sweeps parallelism from `--min-parallelism` up to `--max-parallelism` (doubling by default: 1, 2, 4,
8, ...; `--step-mode linear` for fixed increments), pre-creating `max-parallelism` `IWorkGrain`s once
against a single `TestCluster` so per-step timing isn't skewed by cluster/grain-activation startup
cost. At each step P, exactly P grains run with one dedicated caller each (no same-grain contention
within a step — that's `MailboxContention`'s concern) for `--duration-per-step` seconds, reporting
aggregate calls/s, calls/s/core, a `LatencyHistogram` (folding tail latency into this concern too),
and an efficiency percentage normalized to the first step's calls/s/core.

**Observed finding (validated 2026-07-10, 32-core machine, `--max-parallelism 8
--duration-per-step 2`):** efficiency measured *above* 100% at P=2/P=4 (up to ~164%) before
declining toward P=8 (~116%), rather than monotonically declining from a 100% baseline. This is a
real artifact of using P=1 as the normalization baseline: a single caller pays proportionally more
for fixed overheads (JIT/thread-pool warm-up, one caller not yet keeping the runtime's own
background scheduler threads busy) than a lightly-parallel step does, so P=1's calls/s/core
understates steady-state single-core throughput. Documented as a reporting caveat (read the
efficiency curve's overall shape — rise then plateau/decline — not any single step's absolute
value) rather than "fixed," since it reflects a genuine property of using a real single-thread run
as the baseline instead of an idealized one.

## 12. File structure

```
tests/Quark.Performance/
  Shared/
    LatencyHistogram.cs
    PaddedCounter.cs
    WorkSimulator.cs
    IWorkGrain.cs
    WorkGrainBehavior.cs
    WorkGrainInvokables.cs
    WorkGrainProxy.cs
  MailboxContention/
    MailboxContentionRunner.cs
  Fairness/
    FairnessRunner.cs
    FairnessDiagnosticListener.cs
  SchedulingQuality/
    SchedulingQualityRunner.cs
    SchedulingQualityDiagnosticListener.cs
  ActorLifecycle/
    IActorLifecycleGrain.cs
    ActorLifecycleGrainBehavior.cs
    ActorLifecycleGrainInvokables.cs
    ActorLifecycleRunner.cs
  Backpressure/
    BackpressureRunner.cs
  CoreScalability/
    CoreScalabilityRunner.cs       — added 2026-07-10 follow-up, see §11a
  AllocationBenchmarks.cs
  CacheLocalityBenchmarks.cs      — CacheLocalityBenchmarks + SchedulerShardDistributionBenchmarks
  ActivationLifecycleBenchmarks.cs — added 2026-07-10 follow-up, see §9a
  Program.cs                      — 6 new runner-name branches + 3 new BenchmarkSwitcher entries
  README.md                       — new suite/runner tables and sections
```

No `.csproj` changes — every referenced namespace (`Quark.Diagnostics.Abstractions`,
`Quark.Persistence.Abstractions`, etc.) is already a project reference of `Quark.Performance`.

## 13. Testing / validation

- No new automated tests — matches `GrainCallBenchmarks`/`PingPong`/`AstroSim`/
  `DispatchPipelineBenchmarks` precedent (headless perf harnesses have no dedicated test coverage
  in this project).
- `dotnet build Quark.slnx` must succeed.
- Each standalone runner validated at small scale first (matching PingPong/AstroSim precedent) —
  see the implementation plan's final task for the exact commands.
- The two new BenchmarkDotNet suites validated via
  `dotnet run -c Release --project tests/Quark.Performance -- --filter '*AllocationBenchmarks*' --inProcess`
  and `--filter '*CacheLocality*' --inProcess` / `--filter '*SchedulerShardDistribution*' --inProcess`.

## 14. Risks / open questions

1. **`GC.GetTotalAllocatedBytes(precise: true)` under `--parallelism 1` is still not perfectly
   clean** — background scheduler-worker threads and GC bookkeeping can add small noise between
   snapshots. Mitigation is statistical (percentiles over many iterations), not a hard guarantee.
2. **`--scope scheduler` only exercises the ready-queue gate meaningfully with many distinct,
   mostly-idle grains** (§8) — contradicts a literal "single-grain toggle" reading of the original
   ask; documented explicitly here and in the README so a future reader doesn't "fix" it back to
   one grain and silently lose all scheduler-scope rejections/waits.
3. **Busy-spin burns real CPU across all cores** during these runs (unlike PingPong/AstroSim's
   near-zero-work grains) — called out in the README so a user isn't surprised by 100% CPU during,
   e.g., `Fairness --hot-callers 32`.
4. **BenchmarkDotNet's `[MemoryDiagnoser]` under concurrent (`Task.WhenAll`) benchmark methods** —
   BenchmarkDotNet's own docs note some imprecision measuring allocations across multiple threads
   within one iteration; `AllocationBenchmarks`'s contention/fan-out results should be read as
   directional, not exact deltas.
5. **`ActivationScheduler.ShardFor` is private** — resolved by replicating the documented formula
   rather than reflection; if that formula ever changes, `SchedulerShardDistributionBenchmarks`'s
   local copy will silently drift. A comment cross-references the source file, but there is no
   compile-time guard against drift.
6. **`GrainActivationTable.TryDeactivateAsync`** was dead code before this change (marked
   `// TODO did not called anywhere`) — `ActorLifecycleRunner` is its first real caller. Worth
   knowing this method just became load-bearing benchmark infrastructure, not merely incidentally
   exercised.
7. **`Fairness`'s baseline/with-hot two-phase design** assumes cold-grain call latency is stable
   enough during the baseline window (`--baseline-seconds`, default 3s) to be a fair comparison
   point. If the process is still JIT-warming during that window, the baseline itself may be noisy
   — consider a larger default or discarding the first second of each phase if this proves
   unreliable in practice (same warm-up lesson PingPongRunner's design spec §11 already recorded).
8. **Confirmed by manual validation:** `Backpressure --scope scheduler --scheduler-overload-mode
   RejectWhenFull` can take minutes to tear down (not hang, but a very long finite tail) if
   `--scheduler-ready-queue-capacity` is set very tight relative to `--grains` under sustained
   offered load (reproduced with `--scheduler-ready-queue-capacity 1 --grains 64 --work-us 2000`).
   Root cause: `ActivationScheduler.ScheduleAsync` throws `SchedulerOverloadException` *after* the
   work item has already been written to the activation's mailbox
   (`GrainActivation.PostCoreAsync` writes to `_queue` before calling `_scheduler.ScheduleAsync`) —
   a rejection therefore leaves the item queued rather than dropping it, since `MailboxCapacity`
   stays unbounded in this scope. With no caller-side backoff on rejection (the runner immediately
   retries), a tight capacity under sustained load can accumulate an enormous per-grain backlog
   within seconds; `GrainActivationTable.DisposeAsync()` then drains each of the (sequentially
   disposed) grains' full backlogs during teardown, one busy-spin `--work-us` at a time. A
   moderate parameter ratio (`--scheduler-ready-queue-capacity 2 --grains 128 --work-us 100`,
   verified: completes in ~4s with a healthy accepted/rejected mix) avoids this. Documented as a
   usage caveat in the README rather than "fixed," since the underlying behavior — a rejected
   schedule attempt not un-queueing its already-written mailbox item — is the runtime's real
   behavior under this option combination, not a defect in the benchmark runner.
