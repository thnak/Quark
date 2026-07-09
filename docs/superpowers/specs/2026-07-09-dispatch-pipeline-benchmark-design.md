# Design: Grain-dispatch pipeline benchmark suite

**Date:** 2026-07-09
**Status:** Draft — ready for implementation
**Lives in:** `tests/Quark.Performance/` (existing benchmark project)

## 1. Goal

`PingPong` (`docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md`) already gives one honest
end-to-end number for Quark's real grain-dispatch path: **~756,297 msg/s** (32 pairs, ×2 Akka convention,
32-core machine) against Akka(.NET)'s cited ~50M msg/s local ping-pong figure — roughly a **66x gap**.
That number says *that* Quark is slower; it says nothing about *where* the time goes.

This design adds a second benchmark file, `DispatchPipelineBenchmarks.cs`, with one `[Benchmark]` per stage
of the call path `LocalGrainCallInvoker.InvokeVoidAsync` walks on every call, so the gap can be attributed to
specific stages instead of guessed at. The deliverable is not just code — it's a findings write-up (added to
this spec after the benchmarks are run) ranking the stages by cost, which is what actually answers "why."

## 2. Non-goals

- Fixing anything. This is diagnosis only. Once the stage costs are measured and ranked, whichever stage(s)
  dominate get a **separate, later** spec scoped to that specific fix — building fixes before knowing the
  actual bottleneck risks optimizing the wrong stage.
- Replacing or fixing `GrainCallBenchmarks`'s mislabeled scope (still out of scope, per the PingPong spec).
- Multi-silo / TCP. Same in-process-only rationale as PingPong and AstroSim.
- A literal Quark implementation of Akka's one-way `tell`. Quark has no fire-and-forget grain-call path today
  (verified: `GrainActivation`'s pooled work items have an `_isFireAndForget` field, but nothing in the
  codebase ever sets it `true` — dead code). Stage 9 below measures the *pattern-level* cost of "await a
  completion signal" vs. not, using a synthetic harness shaped like Quark's real queue — not a claim that a
  tell-style API exists to benchmark directly.

## 3. Background: the call path being decomposed

**This is not a serialization story.** It's tempting to assume Akka's ~50M msg/s is reachable because it's
"basically a native call" while Quark/Orleans "serialize every call" and therefore must be slower. Verified
against the code: `LocalGrainCallInvoker.InvokeAsync`/`InvokeVoidAsync` — the paths every in-process grain
call, including `PingPong`, takes — never call `IGrainInvokable.Serialize`/`CodecWriter` at all (the one
`Serialize` call site in `LocalGrainCallInvoker.cs` is inside `InvokeObserverAsync`'s TCP-writeback branch,
unrelated to normal grain calls). This matches the AstroSim spec's documented finding that "in-process grain
calls never serialize." Akka's cited 50M msg/s figure is also its *local* actor benchmark, not `Akka.Remote`
(dramatically slower per Akka's own docs) — so both benchmarks are already apples-to-apples on "no wire, no
bytes." The gap this spec investigates is architectural (mailbox/scheduler round-trip cost, per-call DI scope,
RPC-await semantics — see the numbered list below), not serialization overhead.

`LocalGrainCallInvoker.InvokeVoidAsync` (`src/Quark.Runtime/LocalGrainCallInvoker.cs`), steady state
(activation already exists, no placement/remote routing), does in order:

1. `GrainActivationTable.GetOrCreateAsync` — `ConcurrentDictionary<GrainId, Lazy<ValueTask<GrainActivation>>>`
   lookup + `Lazy.Value` read.
2. `GrainActivation.PostAsync` (non-reentrant path, `PostCoreAsync`):
   a. `ExecutionContext.Capture()` on every call.
   b. Rent-or-allocate a pooled `MailboxWorkItem`, write it to a `Channel<IMailboxWorkItem>`.
   c. `IActivationScheduler.ScheduleAsync` — wakes a worker (per-worker sharded semaphore signal, per the
      recently-merged workstealing scheduler work).
   d. `await item.WaitAsync()` — the caller suspends on an `IValueTaskSource` with
      `RunContinuationsAsynchronously = true`, which **forces the caller's continuation onto the thread
      pool** rather than resuming inline when the worker signals completion.
3. On the worker side, `DrainAsync` dequeues the item and runs the work delegate, which:
   a. `_services.CreateScope()` — a fresh `IServiceScope` per call.
   b. `GrainScopeBinder.BindAndResolveAsync` — 4 DI resolutions (`IActivationShellAccessor`,
      `ICallContextSetter` ×2 calls, optionally `IGrainScopeInitializerRegistry`, `IBehaviorResolver`) plus
      the behavior resolve itself.
   c. The actual behavior method call (near-zero for `PingPongGrainBehavior`).
4. Diagnostics: `Activity.StartActivity`, `IQuarkDiagnosticListener.OnInvocationStart`/`OnInvocationEnd`,
   two `QuarkInstruments` meter `.Add`/`.Record` calls.

The nine stages below (eleven `[Benchmark]` methods — stages 7 and 9 each split into two variants for an
apples-to-apples comparison) isolate pieces of this list so their individual cost is visible instead of
folded into one opaque end-to-end number.

## 4. Harness construction — no full `TestCluster`

Each benchmark builds its own minimal `ServiceProvider` in `[GlobalSetup]`, following the hand-wired pattern
`Quark.Tests.Fault/FaultFixture.cs` already uses (plain `ServiceCollection` + `AddQuarkRuntime()`, no silo
networking):

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddQuarkRuntime();
services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
_sp = services.BuildServiceProvider();
```

This reuses `IPingPongGrain`/`PingPongGrainBehavior` from `tests/Quark.Performance/PingPong/` directly (no
new grain type) — the payload is already a proven zero-work no-op. `AddQuarkRuntime()` registers the real
`ActivationScheduler` (not `SimpleActivationScheduler`), so these benchmarks exercise the actual production
scheduler, including its per-worker wake-signal sharding.

**One `.csproj` change required:** add `<InternalsVisibleTo Include="Quark.Performance"/>` to
`src/Quark.Runtime/Quark.Runtime.csproj` (alongside the existing test-project entries) — stage 3 needs
`GrainScopeBinder.BindAndResolveAsync`, which is `internal`.

## 5. The stage benchmarks

| # | Benchmark | What it isolates | How |
|---|---|---|---|
| 1 | `ActivationTableLookup` | `GrainActivationTable.GetOrCreateAsync` steady-state cost | Pre-activate one grain in `[GlobalSetup]`; benchmark calls `GetOrCreateAsync` with a factory that throws (never invoked) |
| 2 | `ServiceScopeCreateDispose` | Bare `IServiceScope` cost, no resolution | `using var scope = _sp.CreateScope();` — nothing else |
| 3 | `ScopeBindAndResolve` | `GrainScopeBinder.BindAndResolveAsync` — the 4 DI resolutions + behavior resolve | `using var scope = ...; await GrainScopeBinder.BindAndResolveAsync(scope.ServiceProvider, activation, default);` |
| 4 | `ExecutionContextCapture` | `ExecutionContext.Capture()` alone | Tight loop calling `ExecutionContext.Capture()`, discard result |
| 5 | `MailboxRoundTrip` | Channel write + scheduler wake + pooled-item completion signal, non-reentrant | Construct a bare `GrainActivation` directly (`isReentrant: false`) against the DI root; `await activation.PostAsync(() => default);` in a loop |
| 6 | `MailboxRoundTripReentrant` | Same call, but `isReentrant: true` — bypasses the channel/scheduler entirely (inline `PostAsync`) | Same as #5 with `isReentrant: true` — the delta between #5 and #6 is the channel+scheduler+thread-hop cost |
| 7a | `FullInvokeDiagnosticsOff` | Complete `InvokeVoidAsync`, `NullDiagnosticListener` | Resolve `IGrainCallInvoker` from the DI root, call in a loop |
| 7b | `FullInvokeDiagnosticsOn` | Same, with a listener that actually records (not a no-op) | Same DI root, but register a listener whose methods do real work (e.g. `Interlocked.Increment`, matching `BenchmarkDiagnosticListener`) |
| 8 | `FullInvokeVoidAsync` | The complete real number, single-threaded ops/sec | Same as 7a — kept as a distinctly-named row for the summary table; should roughly equal stage 1 + 3 + 5 combined |
| 9 | `ChannelSignalPattern` (baseline) / `ChannelNoSignalPattern` | The RPC-await cost specifically: writing to a channel and awaiting an `IValueTaskSource` completion signal (Quark's real pattern) vs. writing and never waiting | Synthetic `Channel<T>` + background reader shaped exactly like `GrainActivation`'s queue, run both ways — **not** a call into production fire-and-forget code (none exists), see §2 |

All benchmarks: `[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]`, `[MemoryDiagnoser]` —
matching `GrainCallBenchmarks`'s existing job config, plus memory diagnostics since several stages (scope
creation, `ExecutionContext.Capture()`, DI resolution, pooled-item rent-or-allocate) are allocation-sensitive
and BenchmarkDotNet's allocation column will show whether GC pressure — not raw CPU — is the dominant cost
for a given stage.

## 6. Reporting

BenchmarkDotNet's own summary table (mean, allocated bytes) is the primary output — no custom reporting
needed, unlike PingPong/AstroSim's console runners. After running
`dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*'`, append a results
table to this spec (a new §7 "Findings") with each stage's ns/op and allocated bytes, sorted by cost
descending, plus a one-paragraph interpretation: which stage(s) account for most of the ~66x PingPong gap,
and whether the sum of the isolated stages roughly reconciles with stage 8's full-path number (a mismatch
would itself be a finding — e.g. contention effects that only show up under PingPong's concurrent load and
don't appear in these single-threaded microbenchmarks).

## 7. File structure

```
tests/Quark.Performance/
  DispatchPipelineBenchmarks.cs   — all stage benchmarks, [GlobalSetup]/[GlobalCleanup] for the DI root
src/Quark.Runtime/
  Quark.Runtime.csproj             — add <InternalsVisibleTo Include="Quark.Performance"/>
```

No new grain/proxy/invokable types — reuses `Quark.Performance.PingPong.IPingPongGrain` /
`PingPongGrainBehavior` as the zero-work payload throughout.

## 8. Testing / validation

- No new automated tests — matches `GrainCallBenchmarks`/`PingPong`/`AstroSim` precedent (BenchmarkDotNet
  classes and headless perf harnesses have no dedicated test coverage in this project).
- `dotnet build tests/Quark.Performance/Quark.Performance.csproj` must succeed after every stage is added.
- Success criterion: all stage benchmarks run cleanly and produce a BenchmarkDotNet summary table with
  plausible relative ordering (e.g. stage 8's full path should be slower than any single isolated stage,
  not faster) — then the §6 findings table and interpretation get written into this spec as a follow-up
  edit, which becomes the input to deciding what — if anything — gets its own optimization spec next.
