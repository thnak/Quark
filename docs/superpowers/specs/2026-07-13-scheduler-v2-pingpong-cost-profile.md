# ArenaScheduler (V2) — PingPong CPU cost profile

**Date:** 2026-07-13
**Trace:** `dotnet-trace` (`Microsoft-DotNETCore-SampleProfiler`, managed thread-time, ~100 Hz stacks)
**Workload:** `PingPong --v2 --pairs 32 --duration 15` — 32 ping/pong pairs (64 activations), non-reentrant, in-process `TestCluster`, one silo.
**Machine:** 32 logical cores, Linux, .NET 10 (`10.0.201`).
**Raw artifacts:** `pingpong-v2.speedscope.json` (39 MB) + aggregation scripts in the session scratchpad.

> **How to read this.** The sample profiler tags every leaf with a synthetic `CPU_TIME` (on-CPU) or `UNMANAGED_CODE_TIME` (blocked in a syscall/park) pseudo-frame. Numbers below re-attribute that time to the *real* method one frame up, and split **on-CPU work** (813 s of thread-time) from **parked/waiting** time (556 s). Percentages are of the 813 s on-CPU pool unless noted — this is where the machine actually burns cycles.

---

## Headline

**63% of on-CPU time is overhead, not dispatch work** — GC coordination and lock contention, not running grain methods.

| | on-CPU share | what it is |
|---|---:|---|
| GC safepoint polling (`PollGCWorker`) | **38.8%** | threads rendezvousing for GC — allocation-driven |
| `Monitor.Enter_Slowpath` (lock contention) | **24.6%** | one hot `SemaphoreSlim` + the DI container lock |
| Everything else (actual dispatch + park) | 36.6% | invoker, mailbox, DI resolve, behavior call |

The single biggest lever is **GC**, and it's a one-line config change today:

| Config (same build, same workload) | calls/s | Δ |
|---|---:|---:|
| Workstation GC (current default) | 326,622 | baseline |
| **Server GC** (`DOTNET_gcServer=1`) | **560,282** | **+72%** |

Server GC gives per-core heaps and background collection, so the 32 workers stop stalling at shared GC safepoints. The trace above was captured under workstation GC, which is why `PollGCWorker` dominates — **treat the 38.8% as "recoverable by GC config + allocation cuts," not as fixed cost.**

---

## 1. On-CPU exclusive (self) time — top costs

| self time | % CPU | method | verdict |
|---:|---:|---|---|
| 315.5 s | 38.8% | `Thread.PollGCWorker` | GC rendezvous — allocation pressure (see §4) |
| 199.9 s | 24.6% | `Monitor.Enter_Slowpath` | lock contention (see §2) |
| 54.6 s | 6.7% | `ThreadPool.WorkerThreadStart` | ThreadPool spin-up for async-resume spill |
| 43.5 s | 5.3% | `Thread.Sleep` | spin-then-sleep in park/steal backoff |
| 36.3 s | 4.5% | `LowLevelLifoSemaphore.WaitForSignal` | ThreadPool worker parking |
| 21.5 s | 2.6% | `LowLevelLifoSemaphore.Wait` | idem |
| 13.5 s | 1.7% | `LowLevelSpinWaiter.Wait` | spin before park |
| 11.3 s | 1.4% | `CancellationTokenSource.EnterLock` (contention) | CT registration on every `SemaphoreSlim.Wait(ct)` (see §2) |
| 10.2 s | 1.3% | `ArenaScheduler.WorkerLoop` | scheduler outer loop (our code) |
| 9.4 s | 1.2% | `SpinWait.SpinOnce` | park/steal backoff |
| 8.5 s | 1.0% | DI `Dictionary` lookup (`ServiceLookup`) | per-call service resolution |
| 7.6 s | 0.9% | `LocalGrainCallInvoker.InvokeVoidAsync` state machine | our dispatch |
| 3.2 s | 0.4% | DI `ResolveService` (stub) | per-call resolution |

The Quark methods that show up as *self* time are small (`WorkerLoop` 1.3%, `InvokeVoidAsync` 0.9%). **Our own code is cheap; the cost is in what it makes the runtime do** — GC, locks, ThreadPool hops.

---

## 2. Lock contention — `Monitor.Enter_Slowpath` (24.6% CPU)

Attributing the contended-monitor time to its immediate caller (253.8 s total sampled at the slow-path):

| share | caller entering the monitor | which lock |
|---:|---|---|
| 37.5% | `SemaphoreSlim.WaitCore` | **worker park signal** (`_signals[i]`) |
| 31.0% | `SemaphoreSlim.Release` | **worker wake** (same semaphores) |
| 24.1% | DI `ResolveService` | **DI container realized-services lock** |
| 3.1% | `SingleConsumerUnboundedChannel.Writer` | mailbox lane enqueue |
| 0.9% | DI scope `BeginDispose` | per-call scope teardown |

### 2a. The park/wake `SemaphoreSlim` — ~68% of contention (~174 s)

`ArenaScheduler` parks each worker on a per-worker `SemaphoreSlim` and wakes it with `Release()`:

- `src/Quark.Runtime/Scheduling/ArenaScheduler.cs:72` — `private readonly SemaphoreSlim[] _signals;`
- `:259` — worker parks: `_signals[index].Wait(ct)`
- `:211` — producer wakes: `_signals[worker].Release()`
- `:419`, `:444` — in-flight-cap park/wake on the same semaphores

`SemaphoreSlim` takes an **internal `Monitor`** on both `Wait` and `Release`. At this wake rate (hundreds of thousands/s) the wait side (worker) and release side (32 driver/producer threads) collide on that monitor — hence the 68% split across `WaitCore` + `Release`.

Compounding it: **`Wait(ct)` registers a callback on the `CancellationToken` every single park** → the `CancellationTokenSource.EnterLock` contention (1.4% CPU, §1) and `Registrations.Unregister` on wake. The token is only ever used for shutdown, which already `Release()`s every signal explicitly (`:479`).

**Improvement candidates (highest expected payoff):**
1. Replace `SemaphoreSlim` with a lighter park primitive — a kernel `Semaphore`, an `AutoResetEvent`, or a ported `LowLevelLifoSemaphore` (what the CLR ThreadPool itself uses to avoid exactly this). Lock-free fast path, no managed monitor.
2. Drop the `CancellationToken` from the hot `Wait` — park token-free, observe the existing `_cts`/shutdown `Release()` for exit. Removes per-park CT registration + its lock.

### 2b. DI `ResolveService` lock — ~24% of contention (~61 s)

Every non-reentrant call resolves the behavior (and two runtime services — `AddQuarkRuntime` registrations #3 and #5) from a **fresh per-call scope**. `Microsoft.Extensions.DependencyInjection`'s default provider takes a lock around its realized-services / call-site cache. 32 workers resolving concurrently serialize here.

**Improvement candidates:**
1. Resolve fewer services per call — cache the two per-call runtime services on the activation/shell if they're effectively singletons or scope-invariant.
2. Cache the compiled behavior factory per grain-type and invoke it directly, bypassing generic `ResolveService` (the generator already knows the concrete behavior type — a typed factory delegate avoids the container's locked lookup).
3. Longer term: a purpose-built per-call scope that doesn't hit the shared realized-services dictionary.

---

## 3. Dispatch-path inclusive cost (where a call's time goes)

Inclusive on-CPU time for each stage of one grain call (frames overlap — inclusive = self + all callees):

| inclusive | % CPU | stage | file |
|---:|---:|---|---|
| 406.4 s | 50.0% | `ArenaScheduler.WorkerLoop` (whole worker side) | `ArenaScheduler.cs:216` |
| 218.3 s | 26.9% | `RunActivation` (turn body) | `ArenaScheduler.cs:321` |
| 174.3 s | 21.4% | `LocalGrainCallInvoker.InvokeVoidAsync` (call path) | `LocalGrainCallInvoker.cs` |
| 173.0 s | 21.3% | `GrainActivation.PostCoreAsync` (enqueue + wake) | `GrainActivation.cs` |
| 147.5 s | 18.1% | `DrainAndCompleteAsync` | `GrainActivation.cs` |
| 124.1 s | 15.3% | `MailboxWorkItem.ExecuteAsync` (runs behavior + signals completion) | `GrainActivation.cs` |
| 90.8 s | 11.2% | **DI `ResolveService`** | M.E.DI |
| 42.3 s | 5.2% | `GrainScopeBinder.BindAndResolve` | `GrainScopeBinder.cs` |
| 18.4 s | 2.3% | DI `CreateInstance` (behavior ctor via invoke-stub) | M.E.DI |
| 18.1 s | 2.2% | **`Stopwatch.GetTimestamp`** | diagnostics per-call timing |
| 4.4 s | 0.5% | scope `Dispose` | M.E.DI |

Notes:
- **DI is ~13.5% inclusive** across resolve + bind + ctor + dispose — the largest *non-overhead* consumer, and it holds the contended lock in §2b. This is the top algorithmic target after GC and park/wake.
- **`Stopwatch.GetTimestamp` = 2.2%** — per-call invocation timing (`clock_gettime` syscall). Worth gating behind "is any diagnostic listener actually subscribed" so a no-op listener costs zero. (The PingPong harness registers `BenchmarkDiagnosticListener`, so this is partly a benchmark artifact, but real deployments with a listener pay it too.)
- `GetOrActivateAsync` (activation-table lookup) is **0.6%** — the `ConcurrentDictionary` grain lookup is not a bottleneck.

---

## 4. GC pressure (the 38.8%)

`PollGCWorker` is where threads stall at safepoints while a GC is pending; its callers are spread across every allocating dispatch method (`RunActivation` 17%, `PostCoreAsync` 6.7%, `CreateInstance`, dictionary lookups, buffer copies). That distribution = **high steady allocation rate**, not one bad allocation.

Per non-reentrant call we currently allocate, at minimum: a fresh `IServiceScope`, the behavior instance, several async state machines (`InvokeVoidAsync`, `PostCoreAsync`, `MailboxWorkItem.ExecuteAsync`, `DrainAsync`), the closure(s) captured for the mailbox `Func<ValueTask>`, and the completion-signal object.

See §4a for the measured per-call byte decomposition that turns this into a ranked plan.

---

## 4a. Allocation profile — measured B/call (`[MemoryDiagnoser]`, `DispatchPipelineBenchmarks`)

CPU sampling tells us *where cycles go*; this tells us *where bytes go*. Exact managed allocation per pipeline stage on the real `LocalGrainCallInvoker` path (PingPong behavior, both `--job short` and 5-iteration runs agree):

| stage | B/call | reading |
|---|---:|---|
| `ActivationTableLookup` | **0** | grain lookup is allocation-free |
| `ExecutionContextCapture` | **0** | not a target |
| `MailboxRoundTripReentrant` | **0** | inline turn — the baseline |
| `ServiceScopeCreateDispose` | 128 | bare `root.CreateScope()` |
| `ScopeBindAndResolve` | **792** | scope + 4 DI resolutions + behavior construct |
| `MailboxRoundTrip` (non-reentrant) | **742** | channel write + scheduler wake + completion signal |
| `ChannelSignalPattern` | 312 | the forced-async completion signal alone |
| **`FullInvokeVoidAsync` (total)** | **2039** | **the whole call** |

**The 2039 B/call splits into three buckets:**

| bucket | B/call | share | notes |
|---|---:|---:|---|
| **DI scope + resolution** | **792** | **38.8%** | 128 B scope + 664 B resolving the per-*activation* scoped services (shell accessor, call-context setter, memory accessors). Behavior is dep-free here, so ~none of it is the behavior instance. |
| **Mailbox async machinery** | **742** | **36.4%** | `MailboxRoundTripReentrant`=0 B proves *all* of it is channel + scheduler + completion-signal, not the work. ~312 B is the forced-async signal. |
| Outer invoke machinery | ~505 | 24.8% | outer state machine, `InvokeVoidState` box, diagnostics/activity. |

At the server-GC rate (560k calls/s) that's **~1.14 GB/s** of garbage — the direct driver of the §4 GC-poll cost. Cutting the 792 B DI bucket alone drops it by ~39%.

**Key correction to the earlier guess:** the invoker does **not** allocate a per-call closure — it already passes a `readonly struct` state (`InvokeVoidState<TInvokable>`) into a `static` mailbox lambda (`LocalGrainCallInvoker.cs:222`). So the mailbox 742 B is the async state machine + completion signal, not a captured delegate. The real targets are the **scope** and the **signal**, not a closure.

**Ranked (byte-grounded):**
1. **Server GC** — free +72% throughput (doesn't cut bytes, cuts the poll cost). *(config, trivial)*
2. **Treat the activation as the scope** — cache the per-activation scoped deps (shell accessor, call-context setter, `IActivationMemory<T>` accessors) on the shell and skip `root.CreateScope()` on the fast path. Reclaims the whole **792 B (39%)** *and* removes the DI-resolution lock from §2b. *(medium; call-context must still bind per call)*
3. **Pooled completion signal** — replace the per-call `TaskCompletionSource` with a reusable `IValueTaskSource`. Reclaims ~**312 B (15%)**. *(medium; single-await discipline)*
4. **Struct/pooled work-item state machine** for the rest of the mailbox path — ~**430 B (21%)**. *(high; async-resume correctness)*
5. **Singleton dep-free behaviors** (generator knows ctor arity → one shared instance, no construction) + gate `Stopwatch.GetTimestamp`/activity when no listener is subscribed. *(low)*

Pooling the *behavior instance* (the original hypothesis) is **not** on this list: `ScopeBindAndResolve`=792 B for a dependency-free behavior shows the instance is a negligible slice, and pooling is unsafe once a behavior has injected (activation-bound) deps. The scope — not the behavior — is the garbage.

---

## 5. Parked / waiting time (556 s, informational)

Not on-CPU cost — this is threads correctly asleep. Breakdown: `LowLevelLifoSemaphore.WaitForSignal` 38% (ThreadPool workers idle), `WaitNative` 22%, `Monitor.Wait` 17% (SemaphoreSlim timed waits + console logger + `ManualResetEventSlim`), `Monitor.Enter_Slowpath` 10% (blocked, not spinning, on the park semaphore — the §2a lock again, from the other side). Healthy except that the §2a semaphore also shows up here: fixing it reclaims both the spinning (§2) and some of this blocking.

---

## Ranked backlog (for review)

| # | Change | Expected | Effort | Risk |
|---|---|---|---|---|
| 1 | Default/recommend **Server GC** for silos | **+~70%** measured throughput | trivial (config/doc) | none |
| 2 | **Treat the activation as the scope** — cache per-activation scoped deps on the shell, skip `CreateScope()` per call | **~792 B/call (39% of alloc)** + removes DI-resolve lock (§2b) | medium | call-context must still bind per call |
| 3 | Replace park/wake `SemaphoreSlim` with low-level semaphore; **drop `ct` from hot `Wait`** | cut most of 24.6% lock CPU | medium | scheduler correctness — needs the concurrency-stress + lost-wake tests |
| 4 | **Pooled completion signal** (`IValueTaskSource` for the mailbox turn) | ~312 B/call (15% of alloc) | medium | ValueTask single-await discipline |
| 5 | **Struct/pooled work-item state machine** (rest of mailbox path) | ~430 B/call (21% of alloc) | high | async-resume correctness, keep priority lanes working |
| 6 | **Singleton dep-free behaviors** + gate `Stopwatch.GetTimestamp`/activity behind active-listener check | small alloc + ~2% CPU | low | diagnostics fidelity |

**Suggested sequence:** #1 (free, do now) → measure → **#2 (biggest single allocation bucket + kills a lock)** → #3 (park/wake lock) → #4/#5 (mailbox allocations). Re-run this exact trace + the `DispatchPipelineBenchmarks` allocation numbers after each to confirm the shift.

> Note: pooling the *behavior instance* is deliberately **not** in this list — see §4a. The behavior is a negligible allocation slice and pooling is unsafe once it has injected deps; the per-call **scope** (#2) is the real garbage.

### Reproduce

```bash
dotnet build -c Release tests/Quark.Performance/Quark.Performance.csproj
DLL=tests/Quark.Performance/bin/Release/net10.0/Quark.Performance.dll
dotnet-trace collect --format Speedscope --profile dotnet-sampled-thread-time \
  -- dotnet "$DLL" PingPong --v2 --duration 15 --pairs 32
# GC A/B:
DOTNET_gcServer=0 dotnet "$DLL" PingPong --v2 --duration 10 --pairs 32
DOTNET_gcServer=1 dotnet "$DLL" PingPong --v2 --duration 10 --pairs 32
# Per-call allocation decomposition (§4a):
dotnet run -c Release --project tests/Quark.Performance -- \
  --filter "*DispatchPipelineBenchmarks*" --job short --memory
```
