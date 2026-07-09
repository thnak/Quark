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
dotnet run --project tests/Quark.Performance -- PingPong [--pairs N] [--duration SECONDS] [--reentrant] [--bare]
```

Defaults: `--pairs` = `Environment.ProcessorCount` (saturate available cores, matching Akka's
dispatcher-tuned-for-parallelism setup), `--duration` = 10. `--reentrant` (added 2026-07-09, §13) switches
the grain type from `IPingPongGrain`/`PingPongGrainBehavior` to a second, otherwise-identical
`IReentrantPingPongGrain`/`ReentrantPingPongGrainBehavior` marked `[Reentrant]`, for a direct end-to-end
throughput comparison against the default non-reentrant path. `--bare` (added 2026-07-09, §15) goes
further still — bypasses `LocalGrainCallInvoker`/`GrainScopeBinder` entirely, an experimental mode, not a
supported dispatch path.

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

## 11. Findings (run of 2026-07-09): the cumulative-average metric misrepresents warm-up as an ongoing ramp

A `--pairs 32 --duration 20` run (32-core machine, `Environment.ProcessorCount` default) produced a
cumulative-average curve that climbed for the full 20 seconds without visibly flattening:

```
t=1s   657,719 msg/s (x2, cumulative avg)
t=4s 1,025,924 msg/s (x2, cumulative avg)
t=10s 1,195,175 msg/s (x2, cumulative avg)
t=19s 1,240,907 msg/s (x2, cumulative avg)
t=20s 1,237,665 msg/s (x2, cumulative avg)
```

Read at face value this looks like the system takes ~20s to reach steady state. Reconstructing the
**instantaneous** per-second rate from consecutive samples (`count(t) = avg(t)×t/2`, then differencing)
tells a different story:

| t | raw calls this second | instantaneous (x2) |
|---:|---:|---:|
| 1 | 328,860 | 657,719 |
| 2 | 496,491 | 992,982 |
| 3 | 566,192 | 1,132,384 |
| 4 | 660,305 | 1,320,610 |
| 5–19 | ~630K–660K (flat) | ~1.26M–1.32M (flat) |
| 20 | 588,033 (partial-second cutoff) | 1,176,066 |

**The real system reaches steady state by t≈3-4s, not t≈20s.** The apparent 20-second ramp is an artifact
of the reported metric being a *cumulative* average since program start: the two genuinely slow opening
seconds (t=1, t=2) drag the average down, and it takes many seconds of a flat, already-steady-state rate
for a cumulative mean to visually converge toward it. This is a property of the metric, not the runtime —
the underlying per-second throughput is flat from t≈4 onward.

**Root cause of the first ~2 slow seconds:** every grain call goes through `GrainActivation`'s mailbox,
which forces an async continuation onto the thread pool — the queue is created with
`AllowSynchronousContinuations = false` (`src/Quark.Runtime/GrainActivation.cs:95-101`) and completions run
through a `TaskCompletionSource(RunContinuationsAsynchronously: true)` (`GrainActivation.cs:44`, also
`:959`/`:1116`/`:1217`). This is the same cost `DispatchPipelineBenchmarks` isolated as dominant
(`MailboxRoundTrip` 270.7us vs. `MailboxRoundTripReentrant` 13.0us — see
`2026-07-09-dispatch-pipeline-benchmark-design.md` §9). With `--pairs` defaulting to
`Environment.ProcessorCount` (32 here), all 32 pair-driver loops force this thread-pool hop concurrently
from t=0 — more concurrently-suspended continuations than warm worker threads, so .NET's ThreadPool
hill-climbing injects additional threads before every loop can run at full speed. That injection settles
in ~2 seconds, matching exactly where the instantaneous rate stops climbing (JIT tiering from Tier0 to
Tier1 over the same window is a plausible secondary contributor, not separately isolated here).

**Not a bug** — one-time JIT/thread-pool warm-up, not a sustained bottleneck. Steady-state throughput at
`--pairs 32` is ~1.26M-1.32M msg/s (×2 convention), consistent with where the t=20 cumulative average was
still heading.

**Fixed as a result:** `PingPongRunner`'s reporter now also prints a windowed instantaneous rate
(`count` delta over the last ~1s, not since program start) alongside the cumulative average, so a slow
warm-up window is no longer visually indistinguishable from an ongoing ramp in future runs.

## 12. Findings (dotnet-trace, run of 2026-07-09): two lock-contention points invisible to single-threaded microbenchmarks

**Method.** `dotnet-trace collect --profile dotnet-sampled-thread-time -o pingpong.nettrace -- <exe> PingPong
--pairs 32 --duration 15`, launched from process start (not attach) so warm-up is captured. Converted to
speedscope (`dotnet-trace convert --format speedscope`) and parsed the resulting evented (open/close) call
stacks directly to compute self- and inclusive-time per frame across all 58 profiled threads (~869s of
aggregate thread-time over the 15.5s wall-clock run).

**Framework/continuation overhead dominates the shape, as expected — no surprise here.**
`Task.RunContinuations`/`AwaitTaskContinuation.RunOrScheduleAction` account for 83.5% of aggregate
thread-time, `ThreadPoolWorkQueue.Dispatch` 46.3%, `AsyncMethodBuilderCore.Start` 45.1%. This is the same
cost `DispatchPipelineBenchmarks` isolated (§ dispatch-pipeline spec) — every grain call is an `await`, so
this machinery is structural, not a defect.

**Quark's own call chain, by inclusive time — clean nesting, no hidden hot spot:**

```
LocalGrainCallInvoker.InvokeVoidAsync    37.7%
  GrainActivation.PostCoreAsync          32.1%
    ActivationScheduler.RunWorkerAsync   23.2%
      GrainActivation.DrainAsync         13.4%
        MailboxWorkItem.ExecuteAsync     12.0%
          GrainScopeBinder.BindAndResolveAsync   10.7%
            DI ResolveService                    10.3%
```

**Two genuine Quark-side lock-contention points, only visible under real concurrency** (a single-threaded
`ColdStart` loop, as used in `DispatchPipelineBenchmarks`, cannot reveal contention by construction).
Tracing `Monitor.Enter_Slowpath`'s immediate callers directly:

- **`ActivationScheduler`'s worker-wake path**: `RunWorkerAsync`/`EnqueueToShard` →
  `SemaphoreSlim.Release` → `Monitor.Enter_Slowpath` — ~48s + 7s aggregate (2,276 + 326 slow-path entries).
  Real contention on the semaphore(s) used to signal scheduler workers when new work is enqueued.
- **The DI container's shared call-site cache lock**: `GrainScopeBinder.BindAndResolveAsync` →
  `ResolveService` → `Monitor.Enter_Slowpath`, bottoming out in `CallSiteRuntimeResolver.VisitRootCache` —
  ~48-50s aggregate (3,748 entries). Every one of Quark's per-call `IServiceScope`s resolves against the
  *same shared root* `ServiceProvider`'s first-resolve cache; with 32 concurrent pairs hammering that
  continuously, they collide on this one lock.

Together these two account for roughly as much aggregate time as `InvokeVoidAsync` itself (37.7%) — under
concurrency, lock-waiting is comparable in cost to the dispatch logic it sits next to. Plausibly connected
to why `ScopeBindAndResolve`'s `ColdStart` numbers in the dispatch-pipeline spec showed such a wide
median/mean gap (95.9us median vs. 850.0us mean) — consistent with occasional stalls, though that specific
run was single-threaded so it is not identically this contention.

**Caveat worth flagging for whoever reruns this**: `PingPongGrainBehavior` is not marked `[Reentrant]`
(§3 — by design, this benchmark models the non-reentrant/serialized dispatch path, matching most real
grains). `GrainActivation.PostAsync` (`src/Quark.Runtime/GrainActivation.cs:563-580`) special-cases
`ReentrantSchedulingMode.Immediate`: a `[Reentrant]` activation's `PostAsync` calls `workItem()` directly
and returns its `ValueTask` inline — no queue, no forced-async completion signal, no thread-pool hop. Only
the *non-reentrant* path goes through `PostCoreAsync`'s channel-write-then-await-signal machinery (the
270.7us-vs-13.0us gap `DispatchPipelineBenchmarks` measured). So `ValueTask` alone does not make in-process
calls synchronous — reentrancy mode does; PingPong intentionally exercises the serialized path because
that's what most grains use, but it is not a measurement of Quark's fastest possible path.

## 13. `--reentrant` variant (added 2026-07-09): measuring the inline fast path §12 identified

§12 established that `[Reentrant]` activations skip the mailbox channel and forced-async completion signal
entirely (`GrainActivation.PostAsync`, `src/Quark.Runtime/GrainActivation.cs:563-580`). To measure that
gap end-to-end (not just the isolated `MailboxRoundTrip` vs. `MailboxRoundTripReentrant` microbenchmarks in
`DispatchPipelineBenchmarks`), added a second, otherwise-byte-for-byte-identical grain type:

```
tests/Quark.Performance/PingPong/
  IPingable.cs                          — shared `ValueTask PingAsync()` surface, implemented by both grain
                                           interfaces so PingPongRunner.RunPairAsync needs no duplication
  IReentrantPingPongGrain.cs             — IGrainWithStringKey, IPingable
  ReentrantPingPongGrainBehavior.cs       — same no-op body as PingPongGrainBehavior, plus [Reentrant]
  ReentrantPingPongGrainInvokables.cs     — hand-written IGrainVoidInvokable, mirrors PingPongGrainInvokables.cs
  ReentrantPingPongGrainProxy.cs          — hand-written proxy, mirrors PingPongGrainProxy.cs
```

A distinct interface (rather than a second behavior on `IPingPongGrain`) was necessary: `AddGrainBehavior`
derives a grain's `GrainType` key from the interface name by default, so two behaviors on the same interface
would collide on registration. `IPingPongGrain` and `IReentrantPingPongGrain` both now extend `IPingable`,
letting `RunPairAsync` and the pair-array setup stay generic over either variant via a `cli.Reentrant` branch
in `PingPongRunner.RunAsync` — both grain types are registered unconditionally (cheap), only the selected
one is instantiated into pairs.

**Result (32 pairs, 15s, same machine, load average ~5 at run time — see §11's contention caveat for why
that number matters):**

| Mode | Cumulative avg (x2) | Raw calls | Raw call rate |
|---|---:|---:|---:|
| Non-reentrant (default) | 820,205 msg/s | 6,158,079 | 410,102 calls/s |
| `--reentrant` | 2,400,690 msg/s | 18,056,251 | 1,200,345 calls/s |

**~2.9x throughput end-to-end** from skipping the mailbox channel and forced-async completion signal —
smaller than the ~20x gap `DispatchPipelineBenchmarks` measured for the mailbox round trip in isolation,
because at 32-way concurrency both modes still pay for `Task.Run` scheduling, GC, and JIT the same way;
only the per-call dispatch step itself changes. Confirms §12's read of the code (inline execution,
verified, not just inferred) and gives a real comparison number for future reentrancy-related runtime work.

## 15. `--bare` variant (added 2026-07-09): what if per-call DI resolution were removed entirely?

§12's trace found `GrainScopeBinder.BindAndResolveAsync` → DI `ResolveService` at 51.5%/50.5% of aggregate
inclusive time in `--reentrant` mode — comparable in size to everything else combined. This is a "what if"
experiment to put a real number on that, not a guess: a third mode that bypasses `LocalGrainCallInvoker`
and `GrainScopeBinder` entirely.

**Implementation.** `--bare` constructs `GrainActivation` objects directly (`isReentrant: true`, same
constructor `DispatchPipelineBenchmarks` already uses) against a minimal root `IServiceProvider` (just
`AddLogging()` — no grain behaviors, no scopes registered). **One shared** `ReentrantPingPongGrainBehavior`
instance is reused across every activation (safe — it's stateless). Each pair's `IPingable` is a thin
`BareActivationPingable` wrapper: `activation.PostAsync(() => behavior.PingAsync())` — no
`ServiceProvider.GetService` call anywhere on the per-call path. Counting was switched from
`IQuarkDiagnosticListener.Count` (which never fires here — bare mode never touches
`LocalGrainCallInvoker`) to a plain `Interlocked.Increment` inside `RunPairAsync` itself, uniformly across
all three modes (verified: non-reentrant/reentrant numbers are unchanged after this switch, confirming the
listener and the direct counter were counting the same thing all along).

**Explicitly experimental, not a proposal to ship as-is**: real grain behaviors need per-call scoping for
injected dependencies, `IDisposable` cleanup, and per-call state — `--bare`'s "one shared, stateless
behavior instance" sidesteps all of that on purpose to isolate DI resolution as the one variable removed.

**CORRECTED 2026-07-09 (see §16): the first result below was measured with a self-inflicted bottleneck in
the benchmark harness itself, not a Quark runtime property.** All three modes originally counted calls
through one shared `Interlocked.Increment` target. At non-reentrant/reentrant rates that counter's own
cost is negligible next to the mailbox/DI overhead being measured, but at `--bare`'s far higher per-call
rate the shared, contended cache line became the actual bottleneck — worse with more concurrent pairs, not
better. §16 fixes this with per-pair padded counters; the corrected table replaces the one below. Left
here, struck through in spirit but not in fact, so the mistake and its discovery are traceable — see §16
for how it was caught (a user noticed 1-pair vs. 2-pair throughput wasn't close to 2x) and fixed.

~~**Result (32 pairs, 15s; load average ~9 at run time, vs. ~5 for the §13 baseline):**~~

| Mode | msg/s (×2), UNCORRECTED | Raw calls/s |
|---|---:|---:|
| Non-reentrant (default) | 820,205 | 410,102 |
| `--reentrant` | 2,400,690 | 1,200,345 |
| `--bare` | ~~26,283,175~~ → see §16 for the corrected figure | 13,141,587 |

## 16. Bug found and fixed (2026-07-09): the shared call counter was itself the bottleneck at `--bare` rates

**How it was caught.** Running `--pairs 1` vs. `--pairs 2` (same `--duration`) should scale close to 2x —
each pair runs on its own independent `Task.Run` loop, verified via `top -H` to land on genuinely separate
OS threads. It didn't: `--bare` mode showed *no* improvement at all going from 1 to 2 pairs (21.6M → 20.9M
msg/s — flat, if anything slightly down), while non-reentrant/reentrant scaled closer to expected.

**Root cause.** All three modes counted completed calls via `Interlocked.Increment(ref counter[0])` against
one shared array slot, written from every pair's thread on every single call. An isolated test confirmed
the mechanism directly:

| Threads | Shared contended counter | Independent per-thread counters |
|---:|---:|---:|
| 1 | 59,508,490 ops/s | 212,565,312 ops/s |
| 2 | 40,969,527 ops/s | 215,198,910 ops/s |
| 4 | 38,627,172 ops/s | 205,953,242 ops/s |

A shared `Interlocked` target *degrades* as more threads contend for it (cache-line ping-pong under the
MESI protocol) — independent counters don't. At non-reentrant/reentrant rates (hundreds of thousands to a
few million calls/sec) this cost is noise next to the mailbox/DI overhead being measured. At `--bare`'s
~10-20M calls/sec/thread, the shared counter's own contention became the dominant cost — which is exactly
why it was invisible until `--bare` existed, and exactly why 1-vs-2-pair scaling was the tell.

**Fix.** Replaced the one shared counter with one `[StructLayout(Explicit, Size=64)] PaddedCounter` per
pair (`PingPongRunner.cs`) — each pair writes only its own cache-line-padded slot (`Volatile.Write`, no
`Interlocked` RMW needed since no other thread ever touches that slot), and the reporting/summary code
sums all slots (`Volatile.Read`) once per second. Verified: non-reentrant/reentrant numbers are unchanged
by this fix (same order as before, within normal run-to-run variance); `--bare`'s 1-vs-2-pair scaling
went from flat/regressing to positive (22.7M → 28.3M, ~1.24x, at `--pairs 1` vs. `--pairs 2`/`--duration 5`).

**Corrected result (32 pairs, 15s):**

| Mode | msg/s (×2) | Raw calls/s | vs. non-reentrant |
|---|---:|---:|---:|
| Non-reentrant (default) | 847,582 | 423,791 | 1x |
| `--reentrant` | 2,668,288 | 1,334,144 | 3.1x |
| `--bare` | **72,784,029** | 36,392,015 | **85.9x** |

The corrected `--bare` figure (**72.8M msg/s**) is *higher* than Akka's cited ~50M msg/s ping-pong figure,
not "within ~2x" as the uncorrected §15 result claimed — the contention bug was suppressing the true
ceiling far more at 32-way concurrency (32 threads hammering one cache line) than at the 2-thread scale
where it was first caught. Removing DI/scope resolution specifically is now measured at **~27x beyond
`--reentrant`** (72,784,029 / 2,668,288), not the previously-reported ~11x. §15's conclusion direction is
unchanged (DI resolution is the largest identified lever) — only the magnitude was understated.

## 17. Reporting simplified (2026-07-09): dropped the ×2 "Akka-comparable" figure

§5 decided to report `2 × (grain calls / elapsed seconds)` alongside the raw call rate, approximating
Akka's one-way-`tell` convention. In practice, printing two numbers that always differ by exactly a
factor of 2 side by side (`848,231 msg/s (x2, cumulative avg)  |  ... calls/s`) read as more confusing than
informative — feedback after actually using the tool's output. `PingPongRunner` now prints only the raw
grain-call rate (`calls/s`); the ×2 constant-factor relationship to Akka's counting convention (§5) still
holds as a mental-math note for anyone who wants that comparison, but the tool no longer computes or
prints it.

**Every `msg/s (×2)` figure in §11-§16 above predates this change and is unaffected in substance** — they
were all `raw calls/s × 2`; nothing about the underlying measurement or methodology changed, only what a
future run of the tool prints. Read historical `X msg/s (×2)` entries above as `(X/2) calls/s` if
cross-referencing against a post-2026-07-09 run's raw output.
