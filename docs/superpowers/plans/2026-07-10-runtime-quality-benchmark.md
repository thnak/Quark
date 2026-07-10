# Runtime-Quality Benchmark Suite Implementation Plan

**Goal:** Add five new standalone runners (`MailboxContention`, `Fairness`, `SchedulingQuality`,
`ActorLifecycle`, `Backpressure`) and two new BenchmarkDotNet suites (`AllocationBenchmarks`,
`CacheLocalityBenchmarks`/`SchedulerShardDistributionBenchmarks`) to `tests/Quark.Performance/`,
covering contention, fairness, scheduling quality, actor lifecycle, allocation, cache locality,
backpressure, and latency-tail reporting — none of which the existing suite/runners isolate.

**Architecture:** A new `Shared/` folder holds a hand-rolled `LatencyHistogram` (percentile
recorder), a public `PaddedCounter`, a `WorkSimulator` (busy-spin), and one shared hand-wired
`IWorkGrain` reused by four of the five new runners plus `AllocationBenchmarks`. `ActorLifecycle`
and the two BenchmarkDotNet suites bypass `TestCluster` entirely (raw `ServiceCollection` +
`AddQuarkRuntime()`, same pattern as `DispatchPipelineBenchmarks`); the other four runners use
`TestCluster` (same pattern as `PingPong`/`AstroSim`).

**Tech Stack:** .NET 10, BenchmarkDotNet, Quark.Runtime (`SiloRuntimeOptions`, `ActivationScheduler`,
`GrainActivation`, `GrainActivationTable`), Quark.Testing.Harness (`TestCluster`),
Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` — every task below
  implements one section of it.
- No `.csproj` changes — all referenced namespaces are already project references.
- `LatencyHistogram.Record()` always takes microseconds; ms-based diagnostic events are ×1000'd
  first.
- Custom `IQuarkDiagnosticListener`s are registered via
  `services.AddSingleton<IQuarkDiagnosticListener>(instance)` directly, never
  `services.AddQuarkDiagnostics<T>()` — the latter has a documented circular-DI bug (see
  `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md` §5).
- Artificial per-call work uses `WorkSimulator.BusySpinMicroseconds`, never `Task.Delay` — see
  spec §3 for why.
- `dotnet build Quark.slnx` must succeed after every task.
- No new automated tests — matches existing benchmark-project precedent.

---

## File structure

```
tests/Quark.Performance/
  Shared/
    LatencyHistogram.cs                    — Task 1
    PaddedCounter.cs                       — Task 1
    WorkSimulator.cs                       — Task 1
    IWorkGrain.cs                          — Task 1
    WorkGrainBehavior.cs                   — Task 1
    WorkGrainInvokables.cs                 — Task 1
    WorkGrainProxy.cs                      — Task 1
  MailboxContention/
    MailboxContentionRunner.cs             — Task 2
  Fairness/
    FairnessRunner.cs                      — Task 3
    FairnessDiagnosticListener.cs          — Task 3
  SchedulingQuality/
    SchedulingQualityRunner.cs             — Task 4
    SchedulingQualityDiagnosticListener.cs — Task 4
  ActorLifecycle/
    IActorLifecycleGrain.cs                — Task 5
    ActorLifecycleGrainBehavior.cs         — Task 5
    ActorLifecycleGrainInvokables.cs       — Task 5
    ActorLifecycleRunner.cs                — Task 5
  Backpressure/
    BackpressureRunner.cs                  — Task 6
  AllocationBenchmarks.cs                  — Task 7
  CacheLocalityBenchmarks.cs               — Task 8
  Program.cs                               — Task 9
  README.md                                — Task 9
docs/superpowers/specs/, plans/            — this doc + the design spec (already written)
```

---

### Task 1: Shared utilities

**Files:** Create all seven files under `tests/Quark.Performance/Shared/` (see spec §3 for exact
API shapes — `LatencyHistogram.Record(double microseconds)`/`Merge() -> Percentiles`,
`PaddedCounter` as a 64-byte-padded struct, `WorkSimulator.BusySpinMicroseconds(int)`, and the
`IWorkGrain`/`WorkGrainBehavior`/`WorkGrainBehavior_DoWorkInvokable`/`WorkGrainProxy` hand-wired
grain family mirroring `PingPong/IPingPongGrain.cs`'s shape).

- [x] Implement `LatencyHistogram.cs` + `PaddedCounter.cs` + `WorkSimulator.cs`.
- [x] Implement `IWorkGrain.cs` + `WorkGrainBehavior.cs` + `WorkGrainInvokables.cs` +
      `WorkGrainProxy.cs`.
- [x] Build to verify (`dotnet build tests/Quark.Performance/Quark.Performance.csproj`).

### Task 2: `MailboxContention` runner

**Files:** Create `MailboxContention/MailboxContentionRunner.cs`.

- [x] `TestCluster` with `--grains` `IWorkGrain`s; `grains × callersPerGrain` tight-loop workers,
      each with its own `PaddedCounter` slot, timing calls into a shared `LatencyHistogram`.
- [x] 1s progress reporter (cumulative + instantaneous calls/s, matching `PingPongRunner`).
- [x] Final report: calls/s + `LatencyHistogram.Merge()`.
- [x] Build to verify.

### Task 3: `Fairness` runner + listener

**Files:** Create `Fairness/FairnessRunner.cs`, `Fairness/FairnessDiagnosticListener.cs`.

- [x] `FairnessDiagnosticListener` counting `OnSchedulerDrainYielded`.
- [x] Phase 1 (`--baseline-seconds`): cold grains only, feeding `coldBaselineHistogram`.
- [x] Phase 2 (`--duration`): `--hot-callers` hammering one hot grain concurrently with cold
      grains, feeding `coldWithHotHistogram`.
- [x] `--drain-budget` sets `SiloRuntimeOptions.SchedulerDrainBudget`.
- [x] Final report: hot-grain calls/s, baseline vs. with-hot cold latency side by side, drain-yield
      count.
- [x] Build to verify.

### Task 4: `SchedulingQuality` runner + listener

**Files:** Create `SchedulingQuality/SchedulingQualityRunner.cs`,
`SchedulingQuality/SchedulingQualityDiagnosticListener.cs`.

- [x] `SchedulingQualityDiagnosticListener` feeding `OnSchedulerActivationWaited`/
      `OnSchedulerDrainCompleted` into two `LatencyHistogram`s + an items/drain running total.
- [x] `--scheduler-workers` sets `SchedulerMaxConcurrentActivations`.
- [x] Round-robin dispatcher firing calls via `Task.Run` without awaiting completion before the
      next dispatch, `Task.Delay(dispatchIntervalMs)` between dispatches.
- [x] Final report: wait-time distribution, drain-duration distribution, avg items/drain.
- [x] Build to verify.

### Task 5: `ActorLifecycle` grain + runner

**Files:** Create `ActorLifecycle/IActorLifecycleGrain.cs`,
`ActorLifecycle/ActorLifecycleGrainBehavior.cs`, `ActorLifecycle/ActorLifecycleGrainInvokables.cs`,
`ActorLifecycle/ActorLifecycleRunner.cs`.

- [x] `IActorLifecycleGrain`/`ActorLifecycleGrainBehavior` implementing `IActivationLifecycle` with
      no-op hooks (their purpose is only to make the hooks fire).
- [x] Raw `ServiceCollection` + `AddQuarkRuntime()` + `GrainTypeRegistry.Register(...)` (no
      `TestCluster`), resolving `GrainActivationTable`/`IGrainCallInvoker` directly.
- [x] Per-worker loop: unique `GrainId` per iteration → `invoker.InvokeVoidAsync` (create/activate,
      timed) → `activationTable.TryDeactivateAsync` (destroy, timed).
- [x] `--allocations`: forces `--parallelism 1`, wraps each round trip with
      `GC.GetTotalAllocatedBytes(precise: true)` before/after (see spec §7 for why not a
      thread-local counter).
- [x] Final report: creations/sec, destructions/sec, create/destroy latency, bytes/op (with
      `--allocations`).
- [x] Build to verify.

### Task 6: `Backpressure` runner

**Files:** Create `Backpressure/BackpressureRunner.cs`.

- [x] `--scope mailbox`: one target grain, `--callers` tight loops, `MailboxCapacity`/
      `MailboxFullMode` from CLI.
- [x] `--scope scheduler`: `--grains` distinct grains each with one caller loop (see spec §8 for why
      not one hot grain), `SchedulerReadyQueueCapacity`/`SchedulerOverloadMode` from CLI.
- [x] `Wait` mode: record each call's await duration into a `LatencyHistogram`.
- [x] `RejectWhenFull` mode: catch `MailboxFullException`/`SchedulerOverloadException`, count
      accepted/rejected via `PaddedCounter`s.
- [x] Build to verify.

### Task 7: `AllocationBenchmarks` BenchmarkDotNet suite

**Files:** Create `AllocationBenchmarks.cs`.

- [x] `[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)] [MemoryDiagnoser]`, sync
      `Setup()`/`Cleanup()` wrappers (same `InProcessEmit` gotcha as `DispatchPipelineBenchmarks`).
- [x] Reuses `Shared.IWorkGrain`/`WorkGrainBehavior` against a hand-built `ServiceProvider` + direct
      `IGrainCallInvoker` (no `TestCluster`, no proxy).
- [x] `SingleGrainSequential` (baseline), `SingleGrainConcurrentContention` (8 concurrent callers,
      same grain), `NGrainFanOut` (8 concurrent callers, 8 distinct grains).
- [x] Build to verify.

### Task 8: `CacheLocalityBenchmarks` BenchmarkDotNet suite

**Files:** Create `CacheLocalityBenchmarks.cs` (two classes — see spec §10 for why not one).

- [x] `CacheLocalityBenchmarks`: `[Params(2, 4, 8)] ThreadCount`, `UnpaddedConcurrentIncrement`
      (baseline) vs. `PaddedConcurrentIncrement`.
- [x] `SchedulerShardDistributionBenchmarks`: replicates `ActivationScheduler.ShardFor`'s formula
      locally, prints shard-imbalance stats from `[GlobalSetup]`, times `ShardHashComputation`
      across `[Params(1000, 10000, 100000)] GrainCount` × `[Params(4, 8, 16)] ShardCount`.
- [x] Build to verify.

### Task 9: Wire `Program.cs` + `README.md`

**Files:** Modify `Program.cs`, `README.md`.

- [x] Add 5 new runner-name branches (`MailboxContention`, `Fairness`, `SchedulingQuality`,
      `ActorLifecycle`, `Backpressure`) before the `BenchmarkSwitcher` fallback.
- [x] Add `AllocationBenchmarks`, `CacheLocalityBenchmarks`, `SchedulerShardDistributionBenchmarks`
      to the `BenchmarkSwitcher` array.
- [x] README: new BenchmarkDotNet-suite table rows, updated runner-name sentence, 5 new `###`/`####`
      runner sections (lead paragraph, `dotnet run` fence, options table, small-scale sanity-check
      callout, busy-spin CPU-cost note), new "Choosing which tool to run" bullets, new "Design docs"
      bullet linking the combined spec.
- [x] Build to verify.

### Task 10: Manual validation

- [ ] `dotnet build Quark.slnx` succeeds.
- [ ] `dotnet run --project tests/Quark.Performance -- MailboxContention --grains 4 --callers-per-grain 2 --duration 3`
- [ ] `dotnet run --project tests/Quark.Performance -- Fairness --hot-callers 4 --cold-grains 2 --duration 5`
- [ ] `dotnet run --project tests/Quark.Performance -- SchedulingQuality --activations 8 --duration 5`
- [ ] `dotnet run --project tests/Quark.Performance -- ActorLifecycle --parallelism 2 --duration 3 --allocations`
- [ ] `dotnet run --project tests/Quark.Performance -- Backpressure --scope mailbox --mailbox-capacity 10 --mailbox-full-mode RejectWhenFull --callers 8 --duration 3`
- [ ] `dotnet run -c Release --project tests/Quark.Performance -- --filter '*AllocationBenchmarks*' --inProcess`
- [ ] `dotnet run -c Release --project tests/Quark.Performance -- --filter '*CacheLocality*' --inProcess`
- [ ] `dotnet run -c Release --project tests/Quark.Performance -- --filter '*SchedulerShardDistribution*' --inProcess`
- [ ] Confirm each runner prints sane, non-crashing output: no unhandled exceptions, percentiles
      monotonic (p50 ≤ p90 ≤ p99 ≤ p999 ≤ max), accepted/rejected or creation/destruction counts
      reconcile with elapsed duration.
