# Design: Astro-sim throughput benchmark

**Date:** 2026-07-08
**Status:** Draft — ready for implementation
**Lives in:** `tests/Quark.Performance/` (existing benchmark project; not a `samples/` showcase)

## 1. Goal

`tests/Quark.Performance` currently measures raw dispatch overhead (`GrainCallBenchmarks`,
`SerializationBenchmarks`, `StreamingBenchmarks`) with synthetic single-purpose calls. This adds one more
scenario, **AstroSim**, that generates a large, organic volume of real grain-to-grain traffic —
a spatially-chunked N-body simulation — and reports sustained messages/sec, to see how close a realistic
workload gets to the ~90M msg/s ceiling those microbenchmarks establish.

This is a stress-test/benchmark, not a polished sample: no persistence, no client, no visualization.
Console output only, in the style of the existing `LocalStreamingTest`.

## 2. Non-goals

- Physical accuracy of the gravity simulation (chunk-local + aggregate-neighbor is a deliberate
  approximation — see §4 — not a real Barnes-Hut or PM solver).
- Multi-silo / TCP distribution. Single in-process silo only (see §6 for rationale).
- Persistence, reminders, transactions, or any feature outside grain calls.
- A permanent `samples/` entry. If this later proves interesting as a showcase, it can be promoted;
  v1 is scoped to the benchmark project only.

## 3. Chunk & body model

Space is a 3D voxel grid. Each cell is one grain activation:

```csharp
public interface IChunkGrain : IGrainWithStringKey  // key = "x,y,z"
{
    ValueTask TickAsync();
    ValueTask<ChunkAggregate> GetAggregateAsync();
    ValueTask TransferBodyAsync(Body body);
}

public readonly record struct ChunkAggregate(Vector3 CenterOfMass, float TotalMass, int BodyCount);

public struct Body
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Mass;
}
```

- `ChunkGrainBehavior : IGrainBehavior, IChunkGrain` holds its bodies as a `List<Body>` (or array) in
  activation state — an in-grain collection, not sub-grains. No `[GenerateSerializer]` on `Body` /
  `ChunkAggregate`: all calls in this benchmark are in-process (§6), and in-process grain calls never
  serialize. The behavior must be `[Reentrant]` — see §4a for why and what that requires of its state access.
- Grid resolution (`--grid N` → N×N×N chunks) and target body count (`--bodies`) are both CLI-configurable
  (§7), so a run can scale from ~100K bodies (small grid, sanity check) to 10M+ (the target scale) without
  code changes. Bodies are seeded with a uniform-random distribution across the grid at startup.
- Chunk grains are pre-activated up front (one `GetGrain<IChunkGrain>` per cell, cached in an array indexed
  by `(x,y,z)`) so the tick loop never pays first-call activation cost mid-run.

## 4. Per-tick update — where the message volume comes from

Each simulation tick, for every chunk grain, in this order:

1. **Local step.** Compute pairwise gravity among only that chunk's own bodies (O(k²) where k =
   bodies/chunk; k is kept small by chunk sizing, so this stays cheap) and integrate position/velocity by a
   fixed `dt`. Zero cross-grain messages.
2. **Neighbor pull.** Call `GetAggregateAsync()` on each of up to 26 neighbor chunks (3×3×3 minus self;
   fewer at grid edges) and apply each returned `(CenterOfMass, TotalMass)` as one aggregate gravitational
   force on every local body. **This is the dominant, predictable message source**: `chunks × ≤26` calls
   per tick. It's O(1) per neighbor (not a full body-list exchange), which is what keeps it cheap enough to
   sustain at high tick rates while still being a real grain call, not a synthetic ping.
3. **Boundary handoff.** After integration, any body whose position now falls outside its owning chunk's
   bounds is removed locally and handed to the correct neighbor via `TransferBodyAsync(body)`. Sparse and
   irregular relative to step 2 — adds realistic non-uniform traffic on top of the predictable aggregate
   traffic.

The driver runs ticks back-to-back: `await Task.WhenAll(chunks.Select(c => c.TickAsync()))` per step, for a
configured wall-clock duration (default 10s), not a fixed tick count — throughput, not simulation length,
is what's being measured.

### 4a. Concurrency: this grain must be `[Reentrant]`

Chunk A's `TickAsync` awaits a call into chunk B (`GetAggregateAsync`) while chunk B's own `TickAsync` —
running concurrently, dispatched by the same `Task.WhenAll` — awaits a call back into chunk A. By default a
Quark grain processes one call at a time (FIFO mailbox), so this is the classic cross-grain cyclic deadlock:
each grain's single in-flight call is blocked waiting on the other, and neither mailbox can advance to
service the incoming call it needs to unblock. **Verified**: without `[Reentrant]`, a 512-chunk run hangs
permanently inside the very first tick.

`ChunkGrainBehavior` must carry `[Reentrant]` (`Quark.Core.Abstractions.Grains`). Per
`wiki/Lifecycle-and-Failure-Semantics.md`, reentrant behaviors get **no** automatic thread-safety — "calls
interleave, and your state must tolerate that." Concretely, incoming `GetAggregateAsync`/`TransferBodyAsync`/
`SeedAsync` calls can arrive and run on a different thread *while* this chunk's own `TickAsync` is suspended
awaiting a neighbor. `TickAsync` must therefore never hold or mutate the shared `List<Body>` across an
`await`: it snapshots the list into a private array under a short lock *before* the neighbor-await loop,
computes entirely against that private copy, then writes the result back under a second short lock. The
other three methods (`GetAggregateAsync`, `TransferBodyAsync`, `SeedAsync`) do all their own list access
under the same lock and never await while holding it. See the implementation plan for the exact snapshot/
commit shape.

## 5. Throughput measurement

Reuses existing diagnostics infrastructure instead of hand-placed counters in grain code:

```csharp
public sealed class BenchmarkDiagnosticListener : IQuarkDiagnosticListener
{
    private long _count;
    public long Count => Interlocked.Read(ref _count);
    public void OnInvocationEnd(in InvocationEndEvent e) => Interlocked.Increment(ref _count);
}
```

Registered via `services.AddSingleton<IQuarkDiagnosticListener>(listener)` directly — **not**
`services.AddQuarkDiagnostics(listener)`. **Verified**: that helper (`Quark.Diagnostics/
DiagnosticsServiceCollectionExtensions.cs`, itself marked `// TODO did not implemented or used in any
elsewhere` — it has no other caller in the repo) is circular. Its `EnsureComposite` step registers
`IQuarkDiagnosticListener` as a factory that resolves `CompositeDiagnosticListener`, whose constructor
resolves `IEnumerable<IQuarkDiagnosticListener>` — which includes that very factory registration. Resolving
`IQuarkDiagnosticListener` (which `LocalGrainCallInvoker` does on every call) then self-recurses and the
silo never finishes starting. Registering the listener instance directly as `IQuarkDiagnosticListener`
sidesteps the composite machinery entirely and is unaffected by the bug — `AddQuarkRuntime()`'s own
`TryAddSingleton<IQuarkDiagnosticListener>(NullDiagnosticListener.Instance)` no-ops once ours is registered
(order doesn't matter: `TryAddSingleton` before or `AddSingleton` after both leave a single, non-composite
registration). This is a real bug in `Quark.Diagnostics` independent of this benchmark; worth a separate
fix, out of scope here.

The driver samples `Count` once/sec during the run and prints a rolling `messages/sec` line, then a final
summary (total messages, elapsed, average msg/sec, ticks completed, bodies simulated) — matching
`LocalStreamingTest`'s existing console-output style.

`OnInvocationEnd` fires once per grain call the invoker dispatches, so `TickAsync`, `GetAggregateAsync`, and
`TransferBodyAsync` calls are all counted — this is an apples-to-apples count against what
`GrainCallBenchmarks` measures.

## 6. Topology: single in-process silo

All chunk grains run in one process through `LocalGrainCallInvoker` — the same path
`GrainCallBenchmarks`/`StreamingBenchmarks` already use to approach the runtime's raw mailbox ceiling.
Explicitly **not** distributed over TCP: multi-silo placement would bound throughput by network/serialization
cost rather than the in-process dispatch path this benchmark exists to exercise. (A distributed variant is a
plausible future benchmark, not part of this design.)

## 7. CLI surface

New `AstroSim` subcommand alongside the existing `LocalStreaming` one in `Program.cs`:

```
dotnet run --project tests/Quark.Performance -- AstroSim [--bodies N] [--grid N] [--duration SECONDS]
```

Defaults chosen to land around 10M bodies out of the box: `--bodies 10000000 --grid 32 --duration 10`
(32³ = 32,768 chunks → ~305 bodies/chunk average).

**Verified expectation, not a target**: at this density the O(k²) local step and the 26-neighbor loop
(sequential awaits, one round-trip at a time, inside `TickAsync`) dominate a tick's wall-clock cost more
than raw dispatch overhead does — a mid-scale check (1M bodies, 16³ grid, ~244 bodies/chunk) took ~14s for
a *single* tick. The resulting sustained-throughput figure at default settings will land well under the
~90M msg/s ceiling; that gap (realistic per-chunk work + sequential neighbor round-trips vs. a synthetic
tight loop) is itself the interesting result, not a bug to chase before this ships. `--grid` is the knob to
trade off: a larger grid lowers bodies/chunk (cheaper local step) at the cost of more chunks/messages.

## 8. Testing / validation

- No new unit tests — this is a perf harness, consistent with `LocalStreamingTest` and the
  `BenchmarkDotNet` classes having no dedicated test coverage today.
- Manual validation: run at a small scale first (`--bodies 100000 --grid 8`) and confirm bodies stay
  gravitationally bound (don't all fly to infinity or collapse to a point) as a sanity check on the
  integration math, before scaling up to the 10M+ target run.
- Success criterion for this design is a number, not a test: a reported sustained messages/sec figure at
  10M+ bodies, for comparison against the ~90M msg/s ceiling.
- **Already verified during design** (not just planned): this design — `[Reentrant]` +
  snapshot/lock/commit `TickAsync`, direct `IQuarkDiagnosticListener` registration, the full grain/proxy/
  invokable/driver shape — was built and run end-to-end against a live silo at 100K bodies/8³ grid (clean
  run, bounded output, no exceptions) and 1M bodies/16³ grid (same). Two real bugs were caught and fixed in
  the process: the reentrancy deadlock (§4a) and the `AddQuarkDiagnostics` circular-DI bug (§5) — both
  reproduced as genuine hangs, confirmed via `dotnet-dump` thread stacks, not assumed from reading the code.
  The implementation plan reflects the fixed shape directly; a fresh implementer following it should not
  need to rediscover either issue.
