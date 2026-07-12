# Benchmarks

Real, reproducible BenchmarkDotNet numbers for Quark's runtime, DI, serialization, and streaming
paths. All suites live in `tests/Quark.Performance/` and run via:

```bash
dotnet run --project tests/Quark.Performance -c Release -- --filter "*<ClassName>*"
```

**Environment for the numbers below:** Ubuntu 25.04, Intel Xeon Silver 4208 @ 2.10GHz, 2 CPU / 32
logical cores, .NET SDK 10.0.203, .NET 10.0.7 runtime, Release build. Numbers are wall-clock and will
vary by machine — treat the *ratios* between variants as the durable takeaway, not the absolute
microsecond values.

## Contents

- [Opt-in user-service-provider factory](#opt-in-user-service-provider-factory)
- [Stream fan-out](#stream-fan-out)
- [Grain call dispatch](#grain-call-dispatch)
- [Streaming](#streaming)
- [Serialization](#serialization)
- [Allocation](#allocation)
- [Cache locality / scheduler shard distribution](#cache-locality--scheduler-shard-distribution)
- [Activation lifecycle](#activation-lifecycle)

---

## Opt-in user-service-provider factory

`tests/Quark.Performance/UserServiceProviderFactoryBenchmarks.cs` — quantifies the DI cost
[`IGrainUserServiceProviderFactory`](Architecture#opt-in-user-service-provider-factory-per-grain-type-cached-di)
(#162) is designed to remove: re-resolving an expensive, effectively-stateless user dependency graph
(`ExpensiveUserRepository`, simulating a connection-pool-backed repository) on every grain call.
Compares the default fresh-scope-per-call path against the opted-in cached-provider path, both
isolated (`GrainScopeBinder.CreateCallScope` + `BindAndResolve`) and end-to-end (full
`IGrainCallInvoker` round trip). Both variants register behaviors via an explicit compile-time
factory — the shape `BehaviorRegistrationGenerator` emits in production — not the reflection-based
`ActivatorUtilities` fallback, so these numbers reflect real generated code.

| Benchmark | Not opted in | Opted in | Speedup | Alloc reduction |
|---|---:|---:|---:|---:|
| `CreateScopeAndResolve` (isolated DI stage) | 52.1 μs / 9081 B | 1.3 μs / 824 B | **~41x** | **~11x** |
| `FullInvoke` (real end-to-end call) | 103.2 μs / 10329 B | 8.1 μs / 2072 B | **~13x** | **~5x** |

A [dotnet-trace](#profiling-with-dotnet-trace) run of `NotOptedIn_CreateScopeAndResolve` confirms this
isn't reflection overhead — the cost is genuinely `ExpensiveUserRepository`'s own construction, which
the opted-in path builds once per grain type instead of once per call.

## Stream fan-out

`tests/Quark.Performance/StreamFanOutBenchmarks.cs` — measures `StreamSubscriptionRegistry.PublishAsync`
(one publish, N subscribers) against subscriber count. `PublishAsync` fans out concurrently via
`Task.WhenAll`, not a sequential await loop — this suite both measures the pure dispatch overhead
(subscriber snapshot copy, per-subscriber `Task` list, `Task.WhenAll`) as subscriber count scales, and
demonstrates the concurrency payoff: with subscribers that each await a fixed real delay (2 ms,
simulating I/O), publish latency tracks that one delay regardless of subscriber count, not
subscriber-count × delay.

| Subscribers | Pure dispatch overhead (no-op subscribers) | Real-work subscribers (2 ms delay each) |
|---:|---:|---:|
| 1 | 0.31 μs / 152 B | 2.31 ms |
| 10 | 0.73 μs / 368 B | 2.32 ms |
| 100 | 4.5 μs / 1808 B | 2.38 ms |
| 1000 | 43.0 μs / 16208 B | 3.95 ms |

At 1000 subscribers doing real 2 ms work each, total publish latency is still ~4 ms — not the ~2
seconds a sequential-await fan-out would produce. Pure dispatch overhead scales with subscriber count
(expected — snapshot copy and per-subscriber `Task` allocation are genuinely O(N)) but stays in the
tens-of-microseconds range even at N=1000.

## Grain call dispatch

`tests/Quark.Performance/DispatchPipelineBenchmarks.cs` — isolates each stage of
`LocalGrainCallInvoker`'s dispatch path (activation-table lookup, scope create/dispose, DI bind +
resolve, `ExecutionContext.Capture()`, mailbox round trip reentrant/non-reentrant, full invoke with
diagnostics on/off, channel signal patterns) so end-to-end latency can be attributed to a specific
stage instead of guessed at. See
[`docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md`](../../docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md)
for the original motivation (closing the gap to Akka's ping-pong figure).

`tests/Quark.Performance/GrainCallBenchmarks.cs` — coarser-grained grain call throughput.

| Stage | Mean | Allocated |
|---|---:|---:|
| `ActivationTableLookup` — steady-state table lookup | 82.9 ns | 0 B |
| `ServiceScopeCreateDispose` — bare `IServiceScope` create+dispose | 117.2 ns | 128 B |
| `ScopeBindAndResolve` — `GrainScopeBinder.BindAndResolve` (4 DI resolutions + behavior resolve) | 1,228.8 ns | 792 B |
| `ExecutionContextCapture` — `ExecutionContext.Capture()` alone | 5.8 ns | 0 B |
| `MailboxRoundTrip` — real mailbox round trip, non-reentrant | 4,113.5 ns | 661 B |
| `MailboxRoundTripReentrant` — same, `isReentrant: true` (inline, no channel/scheduler hop) | 87.8 ns | 0 B |
| `FullInvokeDiagnosticsOff` — complete `InvokeVoidAsync`, no-op diagnostics | 7,798.8 ns | 1960 B |
| `FullInvokeDiagnosticsOn` — same, with a real (non-no-op) diagnostic listener | 7,740.4 ns | 1960 B |
| `FullInvokeVoidAsync` — same call as `FullInvokeDiagnosticsOff`, named separately per the design's summary table | 7,641.9 ns | 1960 B |
| `ChannelSignalPattern` — write + await forced-async completion signal (Quark's RPC-await pattern) | 4,383.0 ns | 312 B |
| `ChannelNoSignalPattern` — write + return immediately, no completion wait | 103.8 ns | 0 B |

Numbers above are from a confirming re-run. The first pass had flagged `FullInvokeVoidAsync`'s
distribution as multimodal (mValue=4.96) and showed it at 14,750.6 ns — nearly 2x this run's 7,641.9
ns, despite being the same underlying call as `FullInvokeDiagnosticsOff`. `ChannelSignalPattern` was
similarly inflated the first time (8,069.5 ns vs. 4,383.0 ns here). Re-running with no multimodal flag
and all three `FullInvoke*` variants landing within ~2% of each other confirms the first pass was
measurement noise (scheduling/GC interference under `--inProcess`), not a real cost difference — the
table above supersedes the earlier numbers.

The mailbox round trip (`MailboxRoundTrip` vs `MailboxRoundTripReentrant`) is the single largest
attributable cost in the non-reentrant path: ~4.11 μs for the channel write + scheduler wake + thread
hop, vs ~88 ns when reentrancy skips the channel and runs inline — a ~47x difference for that one stage
alone.

| GrainCallBenchmarks | Mean | Allocated |
|---|---:|---:|
| `CounterIncrement` | 37.6 ns | 72 B |
| `HelloGrainCall` | 66.4 ns | 128 B |
| `CounterMultipleIncrements` | 356.3 ns | 0 B |

## Streaming

`tests/Quark.Performance/StreamingBenchmarks.cs` — single-publish, batch-publish, and
subscribe-then-publish latency for in-memory streams (complements the multi-subscriber
[fan-out](#stream-fan-out) numbers above with the single-subscriber baseline).

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `StreamPublishSingle` | 164.0 ns | 56 B |
| `StreamPublishBatch` (100 publishes) | 20.8 μs | 10320 B |
| `StreamSubscribeAndPublish` (subscribe + 50 publishes + unsubscribe) | 24.4 μs | 11752 B |

## Serialization

`tests/Quark.Performance/SerializationBenchmarks.cs` — `CodecWriter`/`CodecReader` ZigZag+LEB128
encode/decode cost for the primitive and generated codecs.

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `SerializeSimple` | 290.6 ns | 72 B |
| `DeserializeSimple` | 311.5 ns | 136 B |
| `SerializeComplex` | 783.0 ns | 112 B |
| `DeserializeComplex` | 880.1 ns | 232 B |
| `SerializeRoundTripSimple` | 576.6 ns | 208 B |
| `SerializeRoundTripComplex` | 1,669.3 ns | 344 B |

## Allocation

`tests/Quark.Performance/AllocationBenchmarks.cs` — steady-state managed allocation per grain call
across the activation/dispatch path.

| Benchmark | Mean | Allocated | Ratio vs. sequential |
|---|---:|---:|---:|
| `SingleGrainSequential` (baseline) | 7.4 μs | 2.02 KB | 1.00x |
| `SingleGrainConcurrentContention` | 36.4 μs | 13.76 KB | 4.95x time / 6.80x alloc |
| `NGrainFanOut` | 31.4 μs | 15.68 KB | 4.26x time / 7.75x alloc |

## Cache locality / scheduler shard distribution

`tests/Quark.Performance/CacheLocalityBenchmarks.cs` — two classes: `CacheLocalityBenchmarks` (false
sharing on a concurrently-incremented counter, padded vs. unpadded, across thread counts) and
`SchedulerShardDistributionBenchmarks` (hash-computation cost for activation-to-shard distribution in
the centralized `ActivationScheduler`, across grain and shard counts).

| ThreadCount | `UnpaddedConcurrentIncrement` | `PaddedConcurrentIncrement` |
|---:|---:|---:|
| 2 | 48.3 ms | 41.7 ms |
| 4 | 141.6 ms | 45.5 ms |
| 8 | 354.6 ms | 65.5 ms |

A clean false-sharing demonstration: unpadded degrades ~7.3x from 2 to 8 threads (cache-line
contention gets worse as more cores fight over the same line), while padded stays roughly flat
(~1.6x) across the same range — cache-line padding is doing its job.

| ShardHashComputation | ShardCount=4 | ShardCount=8 | ShardCount=16 |
|---:|---:|---:|---:|
| GrainCount=1,000 | 3.63 μs | 3.67 μs | 3.62 μs |
| GrainCount=10,000 | 35.8 μs | 35.5 μs | 35.4 μs |
| GrainCount=100,000 | 355.7 μs | 351.6 μs | 352.6 μs |

Cost scales linearly with `GrainCount` (~10x per 10x grains, as expected for a per-grain hash) and is
flat across `ShardCount` — the hash computation itself doesn't care how many shards it's distributing
into.

## Activation lifecycle

`tests/Quark.Performance/ActivationLifecycleBenchmarks.cs` — activation create/deactivate cost. Uses
`RunStrategy.ColdStart` (exactly one real activation per iteration, by design — a fresh `GrainId`
every call forces a genuinely cold `OnActivateAsync`, never a warm reused activation), which
BenchmarkDotNet's `InProcessEmitToolchain` rejects outright ("takes too long to run. Prefer to use
out-of-process toolchains") — and this process couldn't fall back to the out-of-process toolchain
either (ambiguous `Quark.Performance.csproj` name from a second worktree checkout alongside this one).

Fell back to `ProfileRunner`'s `ActivationLifecycle` target — the same `Setup`/operation code, hand-timed
with `Stopwatch` instead of BenchmarkDotNet, run via `dotnet Quark.Performance.dll Profile ActivationLifecycle <seconds>`.
Not BenchmarkDotNet-precision, and the `GrainActivate`/`GrainDeactivate` numbers below carry a real
caveat: a multi-second tight loop calling `GrainActivate` alone never deactivates what it creates (by
design — see the class's own comment), so unlike the original 13-sample `ColdStart` run, a
multi-second loop accumulates tens of thousands of live activations, growing the activation table and
GC pressure as it runs. That's visible directly in the numbers — `GrainActivateDeactivateRoundTrip`
(39.0 μs) coming out *cheaper* than `GrainActivate` alone (95.1 μs) is impossible on its own terms
(round trip is a strict superset of activate), and is exactly what accumulation contamination looks
like once you know to look for it.

| Benchmark | Mean | Allocated | Caveat |
|---|---:|---:|---|
| `GrainActivate` | 95.1 μs/op | 700 B/op | Inflated by unbounded table growth over the run — see above |
| `GrainDeactivate` (excludes untimed `PrepareDeactivate`) | 16.1 μs/op | 1,523 B/op (includes `PrepareDeactivate`) | Alloc figure isn't isolated to deactivation alone |
| `GrainActivateDeactivateRoundTrip` | **39.0 μs/op** | **769 B/op** | Trustworthy — table stays small, no accumulation |

Treat `GrainActivateDeactivateRoundTrip` as the reliable number here — it's also "the real end-to-end
cost" the class's own doc comment calls out. Re-run this suite out-of-process (outside a multi-worktree
checkout) for BenchmarkDotNet-precision numbers on `GrainActivate`/`GrainDeactivate` in isolation.

---

## Profiling with dotnet-trace

`tests/Quark.Performance/ProfileRunner.cs` provides a tight-loop driver for `dotnet-trace`:
BenchmarkDotNet's harness either forks a short-lived out-of-process child (nothing to attach to ahead
of time) or, with `--inProcess`, runs too briefly per iteration for useful CPU sampling. `ProfileRunner`
reuses a benchmark class's own `Setup`/operation/`Cleanup` directly, in-process, in a fixed-duration
loop:

```bash
dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll Profile <target> <seconds>
# targets: UserServiceProviderFactory, StreamFanOut, ActivationLifecycle
```

```bash
dotnet-trace collect --profile dotnet-sampled-thread-time -o out.nettrace -- \
  dotnet tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll Profile UserServiceProviderFactory 8

dotnet-trace report out.nettrace topN -n 25              # exclusive (self) time
dotnet-trace report out.nettrace topN -n 25 --inclusive   # inclusive time
```

On a many-core machine, pin the traced process to one logical processor
(`DOTNET_PROCESSOR_COUNT=1`) before collecting — otherwise idle thread-pool workers parked on
`LowLevelLifoSemaphore.WaitNative` dominate the sample and bury the real hot path.

## Running the full suite

```bash
dotnet run --project tests/Quark.Performance -c Release -- --filter "*"
```

Other `Program.cs` argument modes (`PingPong`, `AstroSim`, `MailboxContention`, `Fairness`,
`SchedulingQuality`, `ActorLifecycle`, `Backpressure`, `CoreScalability`, `LocalStreaming`) run
standalone throughput/quality harnesses outside the BenchmarkDotNet switcher — see each runner's
source for its own reporting format.
