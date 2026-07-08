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
  serialize.
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

Registered via `services.AddQuarkDiagnostics<BenchmarkDiagnosticListener>()` on the benchmark's silo. The
driver samples `Count` once/sec during the run and prints a rolling `messages/sec` line, then a final
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
(32³ = 32,768 chunks → ~305 bodies/chunk average, keeping the O(k²) local step cheap).

## 8. Testing / validation

- No new unit tests — this is a perf harness, consistent with `LocalStreamingTest` and the
  `BenchmarkDotNet` classes having no dedicated test coverage today.
- Manual validation: run at a small scale first (`--bodies 100000 --grid 8`) and confirm bodies stay
  gravitationally bound (don't all fly to infinity or collapse to a point) as a sanity check on the
  integration math, before scaling up to the 10M+ target run.
- Success criterion for this design is a number, not a test: a reported sustained messages/sec figure at
  10M+ bodies, for comparison against the ~90M msg/s ceiling.
