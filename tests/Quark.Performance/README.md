# Quark.Performance

Benchmarks and throughput drivers for the Quark runtime. Two different kinds of tool live in this one
project, dispatched from the same `Program.cs`:

| Kind | What it measures | How it's invoked |
|---|---|---|
| **BenchmarkDotNet suites** | Micro-benchmarks (ns/op, allocations) of specific code paths | `dotnet run -c Release --project tests/Quark.Performance -- [BenchmarkDotNet args]` |
| **Standalone runners** | Sustained real-cluster throughput (msg/s) over a wall-clock duration | `dotnet run --project tests/Quark.Performance -- <RunnerName> [options]` |

`Program.cs` checks `args[0]` for a runner name (`LocalStreaming`, `AstroSim`, `PingPong`); anything else
falls through to `BenchmarkSwitcher`, which hands the remaining args to BenchmarkDotNet.

**Always build/run benchmarks in `Release`** (`-c Release`) — Debug numbers are meaningless and
BenchmarkDotNet will warn (or refuse) if it detects a Debug build.

## BenchmarkDotNet suites

| Suite | File | Measures |
|---|---|---|
| `GrainCallBenchmarks` | `GrainCallBenchmarks.cs` | Raw behavior method dispatch, no activation/DI/mailbox involved |
| `StreamingBenchmarks` | `StreamingBenchmarks.cs` | In-memory stream publish / subscribe+publish via a `TestCluster` |
| `SerializationBenchmarks` | `SerializationBenchmarks.cs` | `CodecWriter`/`CodecReader` serialize/deserialize/round-trip for simple and nested message shapes |
| `DispatchPipelineBenchmarks` | `DispatchPipelineBenchmarks.cs` | Isolates every stage of `LocalGrainCallInvoker`'s dispatch path (activation lookup, DI scope, mailbox round-trip, diagnostics on/off, full invoke) so per-call cost can be attributed to a specific stage instead of guessed at — see `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` |

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
  `dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*' --inProcess --job ColdStart`
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

Two grains per pair volley `PingAsync()` calls back and forth for a fixed duration. Reported `msg/s` is
2x the raw grain-call count, approximating Akka's one-way-`tell` convention (ping leg + pong leg per
round trip) for a like-for-like comparison.

```bash
dotnet run --project tests/Quark.Performance -- PingPong [--pairs N] [--duration SECONDS] [--reentrant]
```

- `--pairs` (default `Environment.ProcessorCount`) — number of ping/pong grain pairs running concurrently
- `--duration` (default `10`) — seconds to run
- `--reentrant` — use a `[Reentrant]` grain variant instead of the default. `[Reentrant]` activations skip
  the mailbox channel and its forced-async completion signal entirely (`GrainActivation.PostAsync` calls
  the work item inline) — run the same `--pairs`/`--duration` with and without this flag to measure that
  gap end-to-end (measured 2026-07-09: ~2.9x, 820K vs 2.4M msg/s at 32 pairs/15s — see
  `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md` §13)

Sanity-check at a small scale first (`--pairs 4 --duration 3`) before trusting the default full-core run.

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

## Design docs and recorded findings

Each throughput tool has a design spec with more background, prior findings, and known caveats:

- `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md`
- `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md`
- `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` (includes a recorded findings
  table from the first suite run — mailbox/channel/scheduler round trip is the dominant per-call cost, not
  DI scope creation or diagnostics)

Corresponding implementation plans are under `docs/superpowers/plans/`.
