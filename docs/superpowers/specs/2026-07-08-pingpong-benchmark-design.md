# Design: Ping-pong throughput benchmark

**Date:** 2026-07-08
**Status:** Draft — ready for implementation
**Lives in:** `tests/Quark.Performance/` (existing benchmark project)

## 1. Goal

Add a `PingPong` benchmark scenario that exercises Quark's **real grain-to-grain dispatch path**
(`IGrainCallInvoker` → `GrainActivationTable` → mailbox/reentrancy dispatch) with trivial, near-zero-work
messages, sustained over a duration, and reports msg/s — directly comparable to the classic Akka(.NET)
ping-pong benchmark (pairs of actors bouncing trivial messages through a tuned dispatcher, historically
cited at ~50M msg/s).

This closes a real gap: none of the three existing `Quark.Performance` benchmarks measure this.
`GrainCallBenchmarks` calls behavior methods directly on plain C# objects — it never touches
`IGrainCallInvoker`, `GrainActivationTable`, the mailbox, or per-call `IServiceScope` construction, so its
~27M ops/sec single-threaded figure (measured 2026-07-08: `CounterIncrement` at 36.77ns/op) is not a grain
dispatch number at all. `StreamingBenchmarks` exercises the pub/sub observer path, not grain calls.
`AstroSim` (see `2026-07-08-astro-sim-benchmark-design.md`) does go through the real invoker path, but does
real per-message physics work — not a trivial-message baseline.

## 2. Non-goals

- Replacing or fixing `GrainCallBenchmarks`'s existing (mislabeled) scope — out of scope here, could be a
  separate follow-up to either rename it or add a real-dispatch variant.
- Multi-silo / TCP distribution. Single in-process silo, matching Akka's *local* ping-pong benchmark
  methodology (not `Akka.Remote`, which the same Akka docs note is dramatically slower).
- An exact numeric match to Akka's 50M msg/s figure. Different runtime, different message-passing model
  (RPC/ask vs. one-way tell — see §4), different hardware. The goal is an honest, real number from the
  actual dispatch path, not a manufactured "beat Akka" result.

## 3. Grain contract

```csharp
public interface IPingPongGrain : IGrainWithStringKey
{
    ValueTask PingAsync();
}
```

One method, no payload, no return value beyond `ValueTask` completion — as close to a zero-work message as
the grain call machinery allows. The behavior implementation does nothing but return:

```csharp
public sealed class PingPongGrainBehavior : IGrainBehavior, IPingPongGrain
{
    public ValueTask PingAsync() => default;
}
```

No `[Reentrant]` needed anywhere in this design (contrast with AstroSim, §4a of its spec) — see §4.

## 4. Concurrency model — why no reentrancy risk

Each pair consists of two grain instances (`ping-{i}`, `pong-{i}`), both implementing `IPingPongGrain`. A
pair's *driver loop* — running on its own `Task.Run`, independent of every other pair — alternates which
instance it calls, so both are genuinely exercised as call targets:

```csharp
IPingPongGrain[] targets = [pongGrain, pingGrain];
for (long i = 0; !cancelled; i++)
{
    await targets[i % 2].PingAsync();
}
```

Unlike AstroSim's `TickAsync` (where chunk A calls into chunk B *while* chunk B's own concurrently-running
`TickAsync` calls back into chunk A — the cyclic-await deadlock condition), **no grain here ever calls
another grain** — both `pingGrain` and `pongGrain` are passive receivers that only ever return. The driver
loop is the sole caller for the entire pair; it just alternates its target each iteration. There is never a
grain-initiated call, so the default non-reentrant, single-call-at-a-time mailbox is never a bottleneck or a
deadlock risk. This keeps the implementation meaningfully simpler than AstroSim.

**Real parallelism** comes from running K independent pairs concurrently, each on its own `Task.Run` — the
same lesson learned fixing AstroSim's tick loop (§ "Fixed post-ship" in the AstroSim spec): trivial
in-process grain calls complete synchronously, so without an explicit `Task.Run` per independently-running
unit of work, everything collapses onto a single thread regardless of how many logical "pairs" exist.

## 5. Message counting: RPC round trip vs. Akka's one-way `tell`

Quark grain calls are request-response RPC (Orleans-style `ask`): `await targets[i % 2].PingAsync()` is one
call that inherently carries a request leg and a response leg. Akka's model is one-way `tell`: actor A sends a
message to B (1 message), B sends a reply back to A (a second, independent message) — a full round trip is
2 messages in Akka's own counting convention.

**Decision:** report `2 × (grain calls / elapsed seconds)` as the msg/s figure, treating each Quark RPC call
as carrying both the "ping" and the "pong" leg. This is an **approximation**, not a literal re-derivation of
Akka's two independent one-way sends — Quark's diagnostic listener only observes one `OnInvocationEnd` per
call, not two. The spec and the runner's console output both state this explicitly so the number is never
read as more directly comparable than it is.

## 6. Throughput measurement

Reuses `BenchmarkDiagnosticListener` from `tests/Quark.Performance/AstroSim/BenchmarkDiagnosticListener.cs`
as-is (same project/assembly, counts `OnInvocationEnd` via `Interlocked.Increment`) — no duplication.
Registered the same verified way AstroSim's spec documents: `services.AddSingleton<IQuarkDiagnosticListener>(listener)`
directly, **not** `services.AddQuarkDiagnostics(listener)` (confirmed circular-DI bug, see
`project_quark_diagnostics_circular_bug` — still unfixed as of this writing; re-check before ever using that
helper).

The driver samples `listener.Count` once/sec and prints a rolling `msg/s (x2 for round-trip)` line, then a
final summary (total round trips, elapsed, average msg/s at both the raw-call rate and the ×2 Akka-comparable
rate, pairs used).

## 7. Topology

Single in-process silo (`TestClusterOptions.InitialSilosCount = 1`, explicit — harness default is 2, same
gotcha as AstroSim), all pairs and the driver in one process through `LocalGrainCallInvoker`. Matches Akka's
*local* ping-pong benchmark, not `Akka.Remote`.

## 8. CLI surface

New `PingPong` subcommand alongside `AstroSim`/`LocalStreaming` in `Program.cs`:

```
dotnet run --project tests/Quark.Performance -- PingPong [--pairs N] [--duration SECONDS]
```

Defaults: `--pairs` = `Environment.ProcessorCount` (saturate available cores, matching Akka's
dispatcher-tuned-for-parallelism setup), `--duration` = 10.

## 9. File structure

```
tests/Quark.Performance/
  PingPong/
    IPingPongGrain.cs           — grain contract
    PingPongGrainBehavior.cs    — no-op behavior (no [Reentrant])
    PingPongGrainInvokables.cs  — hand-written IGrainVoidInvokable (house convention, matches AstroSim)
    PingPongGrainProxy.cs       — hand-written grain proxy
    PingPongRunner.cs           — CLI args, DI wiring, pair spawning, tick loop, reporting
  Program.cs                    — add "PingPong" subcommand dispatch
```

No `.csproj` changes needed — `PingPong/` reuses the same `Quark.Diagnostics.Abstractions` /
`Quark.Persistence.Abstractions` / `Quark.Runtime` / `Quark.Testing` references AstroSim already added.

## 10. Testing / validation

- No new automated tests — matches the `LocalStreamingTest`/AstroSim precedent for this project (headless
  perf harness).
- Manual validation: run at a small scale first (`--pairs 4 --duration 3`), confirm no exceptions and a
  nonzero throughput figure, then run at the default (`--pairs {ProcessorCount} --duration 10`) and record
  the result — both the raw grain-call rate and the ×2 Akka-comparable rate — next to the AstroSim figure and
  the (now-corrected, not-yet-verified-as-90M) `GrainCallBenchmarks` numbers, for an honest three-way
  comparison in the commit/PR description.
- Success criterion is a number, not a test: a reported sustained msg/s figure at `--pairs` =
  `Environment.ProcessorCount`, with the ×2 caveat stated alongside it.
