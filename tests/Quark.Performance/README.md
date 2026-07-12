# Quark.Performance

Benchmarks and throughput drivers for the Quark runtime. Two different kinds of tool live in this one
project, dispatched from the same `Program.cs`:

| Kind | What it measures | How it's invoked |
|---|---|---|
| **BenchmarkDotNet suites** | Micro-benchmarks (ns/op, allocations) of specific code paths | `dotnet run -c Release --project tests/Quark.Performance -- [BenchmarkDotNet args]` |
| **Standalone runners** | Sustained real-cluster throughput (msg/s) over a wall-clock duration | `dotnet run --project tests/Quark.Performance -- <RunnerName> [options]` |

`Program.cs` checks `args[0]` for a runner name (`LocalStreaming`, `AstroSim`, `PingPong`,
`SchedulerSkew`, `MailboxContention`, `Fairness`, `SchedulingQuality`, `ActorLifecycle`,
`Backpressure`, `CoreScalability`); anything else falls through to `BenchmarkSwitcher`, which hands
the remaining args to BenchmarkDotNet.

**Always build/run benchmarks in `Release`** (`-c Release`) — Debug numbers are meaningless and
BenchmarkDotNet will warn (or refuse) if it detects a Debug build.

## BenchmarkDotNet suites

| Suite | File | Measures |
|---|---|---|
| `GrainCallBenchmarks` | `GrainCallBenchmarks.cs` | Raw behavior method dispatch, no activation/DI/mailbox involved |
| `StreamingBenchmarks` | `StreamingBenchmarks.cs` | In-memory stream publish / subscribe+publish via a `TestCluster` |
| `SerializationBenchmarks` | `SerializationBenchmarks.cs` | `CodecWriter`/`CodecReader` serialize/deserialize/round-trip for simple and nested message shapes |
| `DispatchPipelineBenchmarks` | `DispatchPipelineBenchmarks.cs` | Isolates every stage of `LocalGrainCallInvoker`'s dispatch path (activation lookup, DI scope, mailbox round-trip, diagnostics on/off, full invoke) so per-call cost can be attributed to a specific stage instead of guessed at — see `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` |
| `AllocationBenchmarks` | `AllocationBenchmarks.cs` | Allocations/op for a single-grain sequential call vs. same-grain concurrent contention vs. N-grain fan-out — isolates whether contention itself adds allocation beyond the uncontended baseline. See `docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` |
| `ActivationLifecycleBenchmarks` | `ActivationLifecycleBenchmarks.cs` | Precise allocations/op for grain activation alone, deactivation alone, and the full activate+call+deactivate round trip — the BenchmarkDotNet-measured (process-level GC counters, no cross-thread noise) counterpart to `ActorLifecycle --allocations`'s hand-rolled check. See `docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` |
| `CacheLocalityBenchmarks` / `SchedulerShardDistributionBenchmarks` | `CacheLocalityBenchmarks.cs` | False-sharing cost (padded vs. unpadded concurrent counters) and `ActivationScheduler`'s shard-hashing imbalance across grain/shard counts. See `docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` |

Run everything:

```bash
dotnet run -c Release --project tests/Quark.Performance
```

BenchmarkDotNet prompts you to pick which class(es) to run interactively. To skip the prompt, filter by
name (glob, matched against `TypeName.MethodName`):

```bash
# One suite
dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*'

# One benchmark method
dotnet run -c Release --project tests/Quark.Performance -- --filter '*GrainCallBenchmarks.CounterIncrement*'
```

### Known environment gotchas (hit while running these locally)

- **Stale locked worktree ambiguates project discovery.** If BenchmarkDotNet's default toolchain can't
  cleanly resolve which project to restore/build (e.g. a leftover git worktree lock in the tree), pass
  `--inProcess` to run the benchmark in the current process instead of generating a separate project:
  `dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*' --inProcess`.
- **The default `Throughput` run strategy can hang indefinitely** on `DispatchPipelineBenchmarks` under
  `--inProcess`. Work around it with `--strategy ColdStart` (one invocation per iteration instead of
  running until statistical stability):
  `dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*' --inProcess --strategy ColdStart`
  (confirmed 2026-07-10: BenchmarkDotNet's `--job` flag only accepts named job *presets*
  `default|dry|short|medium|long|verylong`, not a run strategy — `--job ColdStart` fails with
  "invalid base job". `--strategy` is the correct flag for `RunStrategy` values.)
  ColdStart trades precision for reliability — expect high variance and multimodal distributions in the
  output; read the **median**, not the mean, as the representative number.

## Standalone throughput runners

These spin up a real in-process `TestCluster` (silo + client), run a workload for a fixed wall-clock
duration, and print a sustained-throughput figure — no BenchmarkDotNet involved, so they finish in
seconds and are the fastest way to sanity-check a change end-to-end.

### `LocalStreaming`

Quick smoke test of in-memory streaming (publish/subscribe, batching, multiple subscribers). No options.

```bash
dotnet run --project tests/Quark.Performance -- LocalStreaming
```

### `PingPong`

Two grains per pair volley `PingAsync()` calls back and forth for a fixed duration. Reports the raw
grain-call rate directly (`calls/s`). An earlier version also reported a `2x` "Akka-comparable msg/s"
figure alongside the raw rate; that was removed 2026-07-09 (design spec §17) both because two numbers
differing by exactly 2x side by side was confusing, and — more importantly — because **the comparison
itself doesn't hold up** (design spec §18): Quark's driver loop is `await`-based `ask` (the caller blocks
for a reply, one call in flight per pair, throughput bounded by round-trip latency); Akka's classic
ping-pong is one-way `tell` (fire-and-forget, sender never waits, no round-trip dependency, effectively
unbounded pipelining). No counting-convention factor fixes that gap — read PingPong's numbers as an
**internal comparison across `--reentrant`/`--bare` modes**, not as a comparison against Akka's or any
other `tell`-based system's published figure.

```bash
dotnet run --project tests/Quark.Performance -- PingPong [--pairs N] [--scheduler-workers N] [--duration SECONDS] [--reentrant] [--bare]
```

- `--pairs` (default `Environment.ProcessorCount`) — number of ping/pong grain pairs running concurrently
- `--scheduler-workers` (default `Environment.ProcessorCount`) — maps to `SiloRuntimeOptions.SchedulerMaxConcurrentActivations`,
  decoupled from `--pairs` and from actual core count. Use a low `--pairs` against a high
  `--scheduler-workers` (e.g. `--pairs 4 --scheduler-workers 128`) to reproduce an idle-churn shape
  where most scheduler shards sit permanently empty — see
  `docs/superpowers/specs/2026-07-12-scheduler-sweep-scaling-investigation.md`. Ignored under `--bare`
  (bypasses `ActivationScheduler`/`TestCluster` entirely). **Note:** passing this flag forces the
  sharded `ActivationScheduler` in place of `AddQuarkRuntime()`'s default `SimpleActivationScheduler`
  fallback (the option is otherwise a no-op — `SimpleActivationScheduler` ignores
  `SchedulerMaxConcurrentActivations` entirely) — safe here because PingPong's volley is client-driven,
  not a nested grain-to-grain call, so it can't hit `ActivationScheduler`'s documented reentrancy
  deadlock (see that type's class remarks and `RuntimeServiceCollectionExtensions.AddQuarkRuntime`).
- `--duration` (default `10`) — seconds to run
- `--reentrant` — use a `[Reentrant]` grain variant instead of the default. `[Reentrant]` activations skip
  the mailbox channel and its forced-async completion signal entirely (`GrainActivation.PostAsync` calls
  the work item inline) — run the same `--pairs`/`--duration` with and without this flag to measure that
  gap end-to-end (measured 2026-07-09: ~3.1x, 424K vs 1.33M calls/s at 32 pairs/15s — see
  `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md` §13)
- `--bare` — experimental, not a supported dispatch path: bypasses `LocalGrainCallInvoker`/
  `GrainScopeBinder` entirely, posting directly to a bare `GrainActivation` backed by one shared behavior
  instance (no per-call DI scope/`ResolveService`). Measures the ceiling if per-call DI resolution were
  removed (measured 2026-07-09, corrected — see §16: ~86x over the default, 424K vs 36.4M calls/s at 32
  pairs/15s — see `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md` §16)

If comparing `--pairs 1` vs. a higher pair count to check scaling, know that call counting uses one
padded counter per pair (`PaddedCounter`, `PingPongRunner.cs`) specifically because a single shared
counter becomes the bottleneck at `--bare` rates (§16) — a lesson worth not re-learning.

Sanity-check at a small scale first (`--pairs 4 --duration 3`) before trusting the default full-core run.

### `SchedulerSkew`

Reproduces a "few hot activations against a large configured N" shape: a small, fixed number of
continuously-volleying ping/pong pairs run against an independently configured, much larger
`SchedulerMaxConcurrentActivations` — most shards sit permanently empty for the whole run. Like
`PingPong --scheduler-workers`, this forces the sharded `ActivationScheduler` in place of the default
`SimpleActivationScheduler` (see that flag's note above — same reasoning applies here). Unlike
`PingPong`, it also reports ready-queue wait-time percentiles (not just aggregate calls/s) — a sharper
signal of idle-worker sweep/park overhead per hop, since throughput alone conflates every other
per-call cost.

```bash
dotnet run --project tests/Quark.Performance -- SchedulerSkew [--hot-pairs N] [--scheduler-workers N] [--duration SECONDS]
```

- `--hot-pairs` (default `2`) — number of continuously-volleying ping/pong pairs
- `--scheduler-workers` (default `128`) — maps to `SiloRuntimeOptions.SchedulerMaxConcurrentActivations`
- `--duration` (default `10`) — seconds to run

Sanity-check at a small scale first (`--hot-pairs 2 --scheduler-workers 32 --duration 3`). See
`docs/superpowers/specs/2026-07-12-scheduler-sweep-scaling-investigation.md` for results and
methodology.

### `AstroSim`

A spatial N-body simulation partitioned into chunk grains on a 3D grid; each tick, every chunk computes
gravity locally and pulls aggregate mass from its (up to 26) neighbor chunks. This is the sustained
message-throughput benchmark at scale (chunks × neighbor calls per tick).

```bash
dotnet run --project tests/Quark.Performance -- AstroSim [--bodies N] [--grid N] [--duration SECONDS]
```

- `--bodies` (default `10_000_000`) — total bodies seeded, uniformly at random across the grid
- `--grid` (default `32`) — grid is `N×N×N` chunks; larger `N` means fewer bodies/chunk (cheaper local
  step) at the cost of more chunks/messages
- `--duration` (default `10`) — seconds to run

Validate at a small scale first — `--bodies 100000 --grid 8` — and confirm bodies stay gravitationally
bound (don't all fly to infinity or collapse to a point) before scaling up to the 10M+ default.

### `MailboxContention`, `Fairness`, `SchedulingQuality`, `ActorLifecycle`, `Backpressure`

Five runners targeting runtime qualities the tools above don't isolate: contention across many
independent grain mailboxes, scheduler fairness under a hot grain, scheduling quality (ready-queue
wait time), actor creation/destruction cost, and backpressure at the mailbox/scheduler layer. All
five report latency percentiles (p50/p90/p99/p999/max) via a shared `LatencyHistogram` — this is
where tail latency lives in this project; there is no separate "tail latency" tool. See
`docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` for full design background.

**Unlike PingPong/AstroSim's near-zero-work grains, these busy-spin real CPU work
(`Shared/WorkSimulator.cs`) to hold a scheduler worker thread for a configured duration — expect
them to peg CPU across all cores while running.**

#### `MailboxContention`

Varies grain count (parallelism across independent, serialized mailboxes) and callers-per-grain
(contention on one grain's single serialized mailbox) independently.

```bash
dotnet run --project tests/Quark.Performance -- MailboxContention [--grains N] [--callers-per-grain M] [--duration SECONDS] [--work-us U]
```

- `--grains` (default `Environment.ProcessorCount`) — independent grains, each with its own mailbox
- `--callers-per-grain` (default `4`) — concurrent callers hammering each grain's single mailbox
- `--duration` (default `10`) — seconds to run
- `--work-us` (default `10`) — busy-spin microseconds per call

Sanity-check at a small scale first (`--grains 4 --callers-per-grain 2 --duration 3`).

#### `Fairness`

One "hot" grain is hammered continuously while a handful of "cold" grains keep receiving light
traffic. Reports cold-grain latency with vs. without the hot grain active — the delta is the
starvation cost `SchedulerDrainBudget`'s yield mechanism is meant to bound.

```bash
dotnet run --project tests/Quark.Performance -- Fairness [--hot-callers N] [--hot-work-us U] [--cold-grains N] [--cold-work-us U] [--cold-call-interval-ms MS] [--baseline-seconds S] [--duration SECONDS] [--drain-budget N]
```

- `--hot-callers` (default `Environment.ProcessorCount`) — concurrent callers hammering the hot grain
- `--hot-work-us` (default `200`) — busy-spin microseconds per hot-grain call
- `--cold-grains` (default `8`) — lightly-called grains
- `--cold-work-us` (default `10`) — busy-spin microseconds per cold-grain call
- `--cold-call-interval-ms` (default `50`) — delay between a cold caller's calls
- `--baseline-seconds` (default `3`) — phase 1 duration: cold grains only, no hot grain
- `--duration` (default `10`) — phase 2 duration: hot grain active alongside cold grains
- `--drain-budget` (default `64`) — maps directly to `SiloRuntimeOptions.SchedulerDrainBudget`; re-run
  at two values to see the fairness/throughput tradeoff

Sanity-check at a small scale first (`--hot-callers 4 --cold-grains 2 --duration 5`).

#### `SchedulingQuality`

A single dispatcher round-robins calls across all activations without awaiting each one to
completion before firing the next (so many stay concurrently in flight), reporting ready-queue wait
time and drain-duration distributions via a custom diagnostics listener.

```bash
dotnet run --project tests/Quark.Performance -- SchedulingQuality [--activations N] [--scheduler-workers N] [--dispatch-interval-ms MS] [--work-us U] [--duration SECONDS]
```

- `--activations` (default `64`) — grains dispatched to in round-robin order
- `--scheduler-workers` (default `Environment.ProcessorCount`) — maps to `SchedulerMaxConcurrentActivations`
- `--dispatch-interval-ms` (default `20`) — delay between successive dispatches
- `--work-us` (default `20`) — busy-spin microseconds per call
- `--duration` (default `10`) — seconds to run

Sanity-check at a small scale first (`--activations 8 --duration 5`).

#### `ActorLifecycle`

Repeatedly forces a fresh grain activation (unique key every iteration), calls it once, then
deactivates it — measuring real create/destroy cost, not a warm-activation call.

```bash
dotnet run --project tests/Quark.Performance -- ActorLifecycle [--parallelism N] [--duration SECONDS] [--allocations]
```

- `--parallelism` (default `Environment.ProcessorCount`) — concurrent create/call/destroy workers
- `--duration` (default `10`) — seconds to run
- `--allocations` — additionally reports bytes/op via `GC.GetTotalAllocatedBytes(precise: true)`
  before/after each round trip; forces `--parallelism 1` (grain completion signals are forced onto
  the thread pool, so a thread-local allocation counter can't cleanly attribute cross-thread work —
  see the design spec) and read the median across iterations, not a single sample

Sanity-check at a small scale first (`--parallelism 2 --duration 3 --allocations`).

#### `Backpressure`

Exercises backpressure at the mailbox layer (`--scope mailbox`, one target grain hammered by many
callers) or the scheduler ready-queue layer (`--scope scheduler`, many distinct, mostly-idle grains
— a single hot grain cannot exercise the scheduler's ready-queue capacity gate, since an
already-scheduled activation's subsequent posts bypass it entirely; see the design spec).

```bash
dotnet run --project tests/Quark.Performance -- Backpressure --scope mailbox|scheduler [--mailbox-capacity N] [--mailbox-full-mode Wait|RejectWhenFull] [--scheduler-ready-queue-capacity N] [--scheduler-overload-mode Wait|RejectWhenFull] [--callers N] [--grains N] [--work-us U] [--duration SECONDS]
```

- `--scope` (default `mailbox`) — `mailbox` or `scheduler`
- `--mailbox-capacity` (default `100`) / `--mailbox-full-mode` (default `Wait`) — mailbox scope only
- `--scheduler-ready-queue-capacity` (default `100`) / `--scheduler-overload-mode` (default `Wait`) — scheduler scope only
- `--callers` (default `Environment.ProcessorCount * 4`) — mailbox scope: concurrent callers on the one target grain
- `--grains` (default `64`) — scheduler scope: distinct grains, one caller loop each
- `--work-us` (default `500`) — busy-spin microseconds per call (deliberately expensive so the queue backs up)
- `--duration` (default `10`) — seconds to run

Under `Wait` mode, each call's own await duration *is* the backpressure signal (reported as latency
percentiles). Under `RejectWhenFull`, accepted/rejected calls/s are reported instead.

**`--scope scheduler --scheduler-overload-mode RejectWhenFull` caveat:** a rejected schedule attempt
happens *after* the work item is already written to the (always-unbounded-in-this-scope) mailbox —
so a rejection doesn't drop the item, it leaves it queued until a later successful schedule for that
same grain drains it. Under sustained offered load with very tight
`--scheduler-ready-queue-capacity` relative to `--grains` (e.g. capacity `1` with 64 grains
hammering as fast as possible), this backlog can grow very large, and teardown (which drains every
grain's full backlog one grain at a time) can then take minutes instead of seconds — not a hang,
just a very long tail. Keep `--scheduler-ready-queue-capacity` and `--grains` in a realistic ratio
(e.g. `--scheduler-ready-queue-capacity 2 --grains 128 --work-us 100` completes in ~4s with a healthy
mix of accepted/rejected calls) rather than deliberately worst-casing both at once.

Sanity-check at a small scale first (`--scope mailbox --mailbox-capacity 10 --mailbox-full-mode RejectWhenFull --callers 8 --duration 3`).

#### `CoreScalability`

Sweeps parallelism from `--min-parallelism` up to `--max-parallelism`, running P independent grains
(one dedicated caller each — no same-grain contention within a step, unlike `MailboxContention`) at
each step and reporting aggregate throughput, throughput/core, and latency percentiles. Efficiency
is normalized to the first step's calls/s/core; a falling efficiency curve marks where scaling stops
being linear (scheduler/thread-pool/hardware saturation) instead of just reporting one number at a
fixed concurrency.

```bash
dotnet run --project tests/Quark.Performance -- CoreScalability [--min-parallelism N] [--max-parallelism N] [--step-mode doubling|linear] [--step N] [--duration-per-step SECONDS] [--work-us U]
```

- `--min-parallelism` (default `1`) — first step's grain/caller count
- `--max-parallelism` (default `Environment.ProcessorCount`) — always included as the final step; pass
  a value above `ProcessorCount` to see the plateau past available cores
- `--step-mode` (default `doubling`) — `doubling` (1, 2, 4, 8, ...) or `linear` (fixed `--step` increments)
- `--step` (default `ProcessorCount / 8`, floor `1`) — increment size, `linear` mode only
- `--duration-per-step` (default `3`) — seconds each parallelism level runs before advancing
- `--work-us` (default `10`) — busy-spin microseconds per call

Sanity-check at a small scale first (`--max-parallelism 8 --duration-per-step 2`).

**Note:** efficiency can measure *above* 100% at low P (observed: up to ~164% at P=2/P=4) before
declining at higher P — the P=1 baseline is a genuine single-thread run, which pays proportionally
more for fixed overheads (JIT/thread-pool warm-up, a single caller not yet saturating the runtime's
own background scheduler threads) than a lightly-parallel step does. Read the overall shape (rises,
then plateaus/declines) as the finding, not whether any single step's efficiency is exactly 100%.

## Choosing which tool to run

- Changed a hot path inside `LocalGrainCallInvoker`, `GrainActivation`, or DI scope resolution? Run
  `DispatchPipelineBenchmarks` — it's built to attribute cost to the exact stage you touched.
  `GrainCallBenchmarks` won't see it (it bypasses activation entirely).
- Changed serialization codecs or added a new `[GenerateSerializer]` shape? Run `SerializationBenchmarks`.
  Note none of the dispatch-path suites touch `CodecWriter` — in-process calls never serialize.
  See `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` §"Serialization
  conclusion" for why the two are measured separately.
- Changed the mailbox, scheduler, or reentrant fast path? Run `PingPong` twice — with and without
  `--reentrant` at the same `--pairs`/`--duration` — for an end-to-end before/after number, not just the
  isolated `MailboxRoundTrip`/`MailboxRoundTripReentrant` stages in `DispatchPipelineBenchmarks`.
- Want a quick, whole-system sanity check of grain-call throughput under real concurrency (not a
  microbenchmark)? Run `PingPong` — it's the fastest of the three real-cluster tools to iterate on.
- Need sustained throughput at scale with realistic per-call work and neighbor fan-out? Run `AstroSim`.
- Changed the in-memory streaming provider? `LocalStreaming` for a fast console check, or
  `StreamingBenchmarks` for BenchmarkDotNet-grade numbers.
- Changed `MailboxCapacity`/`MailboxFullMode`? Run `Backpressure --scope mailbox` under both
  `--mailbox-full-mode` values.
- Changed `SchedulerDrainBudget`? Run `Fairness` at two `--drain-budget` values for the
  fairness/throughput tradeoff.
- Changed `SchedulerMaxConcurrentActivations` or the ready-queue's sharding/work-stealing? Run
  `SchedulingQuality` varying `--scheduler-workers`, and `MailboxContention` varying `--grains`.
  For behavior specifically at N configured well above core count, or a few hot activations against
  a large N, run `PingPong --pairs 4 --scheduler-workers {32,64,128}` and `SchedulerSkew` — see
  `docs/superpowers/specs/2026-07-12-scheduler-sweep-scaling-investigation.md`.
- Changed grain activation/deactivation hooks or `GrainActivationTable`? Run `ActorLifecycle`
  (with `--allocations` if the change could affect per-activation allocation), or for a precise,
  BenchmarkDotNet-measured bytes/op split between activate/deactivate/round-trip, run
  `dotnet run -c Release --project tests/Quark.Performance -- --filter '*ActivationLifecycleBenchmarks*' --inProcess --strategy ColdStart`
  (`GrainDeactivate` requires `ColdStart` — see its class doc for why; read the
  `Toolchain=InProcessEmitToolchain` row, not the `Default` row, which reports `NA` here for the
  same reason `DispatchPipelineBenchmarks` documents above).
- Suspect false sharing, or changed `ActivationScheduler`'s shard-hashing formula? Run
  `--filter '*CacheLocality*'` / `--filter '*SchedulerShardDistribution*'`.
- Changed anything on the mailbox/scheduler hot path and want an allocation regression check? Run
  `--filter '*AllocationBenchmarks*'`.
- Want to know how throughput/latency scale as core count or offered parallelism increases (not just
  one number at a fixed concurrency)? Run `CoreScalability` — the efficiency column shows exactly
  where scaling stops being linear.

## Design docs and recorded findings

Each throughput tool has a design spec with more background, prior findings, and known caveats:

- `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md`
- `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md`
- `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` (includes a recorded findings
  table from the first suite run — mailbox/channel/scheduler round trip is the dominant per-call cost, not
  DI scope creation or diagnostics)
- `docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md` — covers
  `MailboxContention`, `Fairness`, `SchedulingQuality`, `ActorLifecycle`, `Backpressure`,
  `AllocationBenchmarks`, and `CacheLocalityBenchmarks`/`SchedulerShardDistributionBenchmarks` in
  one combined doc, since they share the `LatencyHistogram`/`IWorkGrain` machinery in `Shared/`

Corresponding implementation plans are under `docs/superpowers/plans/`.
