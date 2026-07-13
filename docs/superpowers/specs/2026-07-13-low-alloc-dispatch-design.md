# Low-allocation dispatch: activation-scoped behaviors + handle mailbox

**Date:** 2026-07-13
**Status:** design — approved direction, not yet implemented
**Motivates:** the two dominant per-call allocation buckets measured in
`2026-07-13-scheduler-v2-pingpong-cost-profile.md` §4a.

## Motivation

Measured allocation on the real dispatch path (`DispatchPipelineBenchmarks`, `[MemoryDiagnoser]`):
**2039 B per grain call**, splitting into two near-equal buckets plus a tail:

| bucket | B/call | share | attacked by |
|---|---:|---:|---|
| DI scope + per-call resolution | 792 | 38.8% | **Part A** — activation-scoped behaviors |
| Mailbox async (channel + wake + completion signal) | 742 | 36.4% | **Part B** — handle mailbox |
| Outer invoke machinery | ~505 | 24.8% | partial (both) |

At 560k calls/s (server GC) that is ~1.14 GB/s of garbage — the direct driver of the
38.8%-of-CPU GC-poll cost. The two parts are **orthogonal** and ship independently.

Guiding constraint (from the maintainer): **do not break the existing model; keep the
developer focused on writing the grain.** Hence a *dual* behavior model, not a replacement.

---

## Part A — Dual behavior model (kills the 792 B for opted-in grains)

### The two models, side by side

| | `IGrainBehavior` (existing, default) | `IActivationBehavior` (new, opt-in) |
|---|---|---|
| Instance lifetime | **per call** — constructed fresh, discarded | **per activation** — constructed once, reused, disposed on deactivation |
| DI scope | fresh `IServiceScope` per call | **one** activation scope for the whole activation |
| Mutable instance fields | forbidden (QRK0020/0021) — state goes in `IActivationMemory<T>` | **allowed** — fields *are* the activation state |
| Call-context | bound into the per-call scope | **ambient** via `IGrainContext.CallContext` (set per turn) |
| Per-call alloc | 792 B (scope + resolve + construct) | **~0** steady-state |
| Best for | the common case; stateless handlers; maximum isolation | hot-path grains; naturally stateful actors; Orleans-style migration |

Both remain POCOs with constructor injection. Choosing a model is a one-word change to the
interface list — nothing else in the grain changes.

```csharp
// Default, unchanged — per call, stateless, analyzer-enforced:
public sealed class CartBehavior(IActivationMemory<CartState> state) : ICartGrain { ... }

// Opt-in — per activation, fields hold state, cheap dispatch:
public sealed class CounterBehavior : ICounterGrain, IActivationBehavior
{
    private long _count;                     // legit: lives for the activation
    public Task<long> IncrementAsync() => Task.FromResult(++_count);
}
```

### Runtime shape

- **Activation scope.** `GrainActivation` gains an optional `IServiceScope` created lazily at
  first activation for `IActivationBehavior` grain types, disposed in the shell's deactivation
  path (the same place `IManagedActivationMemory<T>.Destroy` already runs). Per-call grains keep
  the existing per-call scope untouched.
- **One construction.** For an `IActivationBehavior`, the generator's typed factory runs **once**
  from the activation scope; the instance is cached on the shell. Per call: set ambient
  call-context → invoke the cached instance. No scope, no construct, no locked `ResolveService`.
- **Ambient call-context.** `ICallContext` (caller id, idempotency key, cancellation) is no longer
  a resolvable per-call service for this model — it would freeze at activation time. It is set on
  the shell at the start of each turn and read via `IGrainContext.CallContext`.
  - Non-reentrant: turns are serial → a single field on the shell is safe.
  - `[Reentrant]`: concurrent turns → carry the context on the work item / `AsyncLocal`, not a
    shared field.

### Analyzer changes (`BehaviorStateAnalyzer`)

- **QRK0020 / QRK0021** (mutable instance field / writable auto-property): **suppressed** for types
  implementing `IActivationBehavior` — instance state is the point of this model.
- **QRK0022** (mutable static): **still fires** — statics are shared across *all* activations and
  remain wrong in both models.
- **New QRK0023 (Warning), optional:** an `IActivationBehavior` that is also `[Reentrant]` and holds
  mutable non-readonly fields → warn about unsynchronized concurrent access across interleaved turns.

### Generator + DI

- `BehaviorRegistrationGenerator` already emits a typed factory per behavior. It additionally emits a
  **lifetime flag** (per-call vs per-activation) based on whether the class implements
  `IActivationBehavior`, so the runtime selects the dispatch path with no reflection.
- `AddGrainBehavior<,>()` records the flag; hand-wired registrations detect the interface at
  registration time.

### What Part A reclaims

The full **792 B/call** for opted-in grains, **plus** removes the locked DI `ResolveService`
contention (cost-profile §2b) for those grains. Per-call grains are unchanged.

---

## Part B — Handle mailbox (kills the 742 B)

Replaces the per-activation `Channel<IMailboxWorkItem>[]` lanes (P3a) with a pooled, value-type
handle design. `MailboxRoundTripReentrant` = 0 B proves the entire 742 B is this machinery, not the
work — so the ceiling here is near-zero steady-state allocation.

### Structures

```csharp
readonly struct MessageHandle {          // 16 B — lives in the ring slot, no per-message heap node
    public readonly int  DescriptorId;   // index into the descriptor pool
    public readonly int  Sequence;       // ABA / validity guard
    public readonly byte Priority;       // lane
}

sealed class MessageDescriptor {         // pooled; Reset() on return
    public IInvokablePayload Payload;        // the invokable
    public PooledValueTaskSource Completion; // reusable IValueTaskSource — replaces the per-call TCS
    public CancellationToken Ct;
    public CallContext Context;
}
```

### Design decisions specific to Quark

- **Single-consumer.** A non-reentrant activation drains serially → exactly one consumer, many
  producers = a bounded **MPSC** ring (Vyukov-style, per-slot sequence numbers). Cheaper and
  allocation-free vs `System.Threading.Channels`, which is general-purpose and allocates nodes.
- **Priority lanes stay as 4 rings**, not one — highest-non-empty-first drain stays trivial and
  FIFO-within-lane (preserves P3a semantics). One ring can't reorder by priority.
- **Backpressure = ring capacity.** The bounded ring *is* the in-flight bound; this subsumes the
  earlier per-worker in-flight cap for the mailbox side.
- **Completion via `PooledValueTaskSource`** (`ManualResetValueTaskSourceCore`-based), rented per
  call, returned when the awaiter completes — removes the ~312 B `TaskCompletionSource`.
- **Async-resume preserved.** The descriptor / signal returns to the pool at *continuation*, not at
  drain, so a suspended turn keeps its descriptor until it truly completes.
- **Payload pooling is the hard part.** The invokable is already a `struct`; boxing it into
  `IInvokablePayload` per call is an allocation. True zero requires **per-invokable-type pooled
  slabs**, which must be **generator-emitted** (AOT-safe, no reflection) — the generator already
  enumerates every invokable type. This is the one slice with real correctness risk and is scoped
  last.

### What Part B reclaims

Up to the full **742 B/call** for every non-reentrant grain (both behavior models benefit), with the
pooled-signal slice (~312 B) landing first and the payload-slab slice (~430 B) last.

---

## AOT / trim

- No reflection on any hot path: factories, lifetime flags, and payload slabs are all
  generator-emitted. `ActivatorUtilities.CreateInstance` remains only the hand-wired fallback.
- Pooled generics are closed at compile time per invokable/behavior type.
- Only `Interlocked`/`Volatile` in the ring; no `DynamicMethod`, no `ISerializable`.

## Phased plan (build order)

1. **Server GC default/recommendation** — free +72%, independent, ship immediately (config/doc).
2. **Part A** — dual behavior model. Bigger win (792 B + a lock), lower risk (no lock-free code, no
   pooling). Slices: (a) `IActivationBehavior` + activation scope + one-construction path ✅;
   (b) ambient call-context (partial — idempotency key refreshed per turn in `BindActivationBehavior`);
   (c) analyzer QRK0020/21 suppression + QRK0022 kept + QRK0023 ✅;
   (d) generator lifetime flag (currently a one-time `IsAssignableFrom` check per activation);
   (e) benchmarks + trace re-run to confirm the DI bucket is gone ✅.

   **Slice (a) — DONE (2026-07-13).** `IActivationBehavior` marker + a single activation-lifetime
   `IServiceScope` + one cached behavior instance in `GrainActivation` (constructed once via
   `EnsureActivationBehavior`, reused per call via `BindActivationBehavior`, disposed on deactivation);
   `LocalGrainCallInvoker` fast-path branch; reentrant + `IActivationBehavior` throws `NotSupportedException`.
   Measured on `DispatchPipelineBenchmarks`: **2039 B/call → 1253 B/call (−786 B, −38.5%)**, mean latency
   **14.2 µs → 6.8 µs**. The −786 B matches the predicted 792 B DI bucket; the residual 1253 B is the
   mailbox-async + outer-invoke buckets that **Part B** targets. Covered by
   `ActivationScopedBehaviorTests` (instance reuse, field persistence, scope disposal + `OnDeactivate`
   on the same instance, fresh-activation reset, reentrant-throws).

   **Slice (c) — DONE (2026-07-13).** `BehaviorStateAnalyzer` now detects `IActivationBehavior` in the
   type's interface set: QRK0020 (mutable instance field) and QRK0021 (writable auto-property) are
   suppressed for it (per-activation fields are legitimate state), while QRK0022 (mutable static) still
   fires for both models. New **QRK0023** (Warning) flags `[Reentrant]` + `IActivationBehavior` at build
   time, surfacing the runtime `NotSupportedException` earlier. Registered in
   `AnalyzerReleases.Unshipped.md`; covered by 7 new `BehaviorStateAnalyzerTests` (23 total green).
   Still open: generator lifetime flag (d), reentrant support.
3. **Part B** — handle mailbox. Slices: (a) `PooledValueTaskSource` completion (~312 B) behind the
   existing mailbox API; (b) MPSC ring + `MessageHandle`/`MessageDescriptor` replacing the lanes,
   preserving priority + async-resume + backpressure; (c) generator-emitted payload slabs (~430 B).
   Re-run trace + `DispatchPipelineBenchmarks` after each slice.

   **Slice (a) — DONE (2026-07-13), re-scoped.** The premise "swap the per-call `TaskCompletionSource`
   for a pooled `IValueTaskSource`" turned out to be a **no-op**: the mailbox already pools its
   completion signal via `ManualResetValueTaskSourceCore<bool>` inside the thread-pooled
   `MailboxWorkItem` classes (`GrainActivation.cs` ~line 1120). The `TaskCompletionSource` at line 44
   is the per-activation *deactivation* signal, fired once per lifetime — not per call. So the ~312 B
   the slice targeted was already gone.

   Measuring the real activation-scoped residual by type (in-process `GCAllocationTick` listener,
   `AllocByTypeProfiler`) showed the largest *reducible* bucket was **async state-machine boxes**
   (`AsyncValueTaskMethodBuilder` continuation boxes on the dispatch hot path), ~470 B/call. Fixed by
   annotating the hot async methods with `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]`
   / `PoolingAsyncValueTaskMethodBuilder<>`:
   `LocalGrainCallInvoker.InvokeVoidAsync`/`InvokeAsync`, and `GrainActivation.PostCoreAsync<TState>`
   / `PostCoreAsync<TState,TResult>` / `PostCoreAsync` (non-generic). Boxes are now rented from the
   builder's per-thread pool and returned on completion.

   Measured (`AllocByTypeProfiler`, workstation GC): activation-scoped **1013 B/call → 585 B/call
   (−428 B, −42%)**; the async-box types disappear from the top-15 entirely. Throughput up (~670k vs
   ~588k calls/s over 5 s). Full unit suite green (548/550; the 2 failures are wall-clock reminder
   timing tests flaky under full-suite CPU load, pass in isolation, and don't touch the dispatch path).

   Residual 585 B is now dominated by **scheduler-side** costs, not dispatch:
   `SemaphoreSlim.WaitUntilCountOrTimeoutAsync` (159 B — legacy-scheduler park/wake; ArenaScheduler
   avoids it), `TaskNode` (86 B), `StatelessWorkerPoolPolicy` closure (60 B),
   `GetOrActivateAsync`'s `<>c__DisplayClass26_0` capture (52 B — clean follow-up, deferred for
   activation-table concurrency safety), `CompletionActionInvoker` (32 B). Slices (b) MPSC ring and
   (c) payload slabs remain; much of the original 742 B mailbox estimate is already reclaimed.

   **Slice (b) — DONE (2026-07-13), REDIRECTED away from the MPSC ring.** Before writing the ring,
   the residual was re-measured by *full* type name (`AllocByTypeProfiler`, disambiguating `Boolean]`
   → `AsyncStateMachineBox[…]`, etc.). Ground truth: **zero `System.Threading.Channels.*` allocation
   in the residual.** The `Channel` lanes with pooled work items are already allocation-free per op at
   steady state, so a Vyukov MPSC ring replacing them would reclaim ~0 measurable bytes — slice (a) +
   the pooling builder had already absorbed the mailbox-async bucket the original 742 B estimate
   attributed to Part B. The whole 585 B was **scheduler + placement + activation-table**, not the
   mailbox. Rather than ship a large lock-free rewrite against a dead target, slice (b) fixed the three
   real allocators on the dispatch hot path:

   1. **`StatelessWorkerRouter.TryGetPolicy`** passed the `ResolvePolicy` *instance* method group to
      `ConcurrentDictionary.GetOrAdd` on every call, allocating a fresh `Func<GrainType,
      StatelessWorkerPoolPolicy?>` delegate per call even on cache hits — and it runs for *every* grain
      call (non-stateless-worker grains cache a null policy). Fixed by caching the delegate in a field
      (`_resolvePolicy`). **−65 B/call, all grains.**
   2. **`LocalGrainCallInvoker.GetOrActivateAsync`** allocated `() => CreateActivationAsync(grainId,
      ct)` (a `<>c__DisplayClass` + `Func<ValueTask<GrainActivation>>`) before `GetOrCreateAsync`'s
      fast path ran, so it was paid on every cache hit. Added a state-passing
      `GrainActivationTable.GetOrCreateAsync<TState>` overload (static delegate + struct-tuple state,
      identical `Lazy<Task>` dedup semantics; the `Lazy` closure is created only on a miss). **−111
      B/call.**
   3. **`ActivationScheduler.RunWorkerAsync`** parked idle workers on `SemaphoreSlim.WaitAsync(ct)`,
      whose slow path allocates an `AsyncStateMachineBox` + `CancellationPromise<bool>` (+ downstream
      `TaskNode`/`CompletionActionInvoker`) — ~380 B/call in a hot ping-pong where the worker parks and
      wakes ~once per call. Added a **bounded, unregistered spin-before-park**: re-check the shards for
      a few microseconds (until `SpinWait.NextSpinWillYield`) before registering idle or parking. Under
      load the next item lands inside the spin window so the worker never parks (no alloc, lower
      latency); a genuinely idle system exhausts the spin and parks promptly (no busy-wait). The spin
      is pure optimism in front of the original push-idle → re-check → park race guard, so the lost-wake
      invariant is unchanged. **−~380 B/call.**

   Cumulative (`AllocByTypeProfiler`, workstation GC): activation-scoped **585 → 38 B/call (−93.5%)**;
   per-call **1383 → 1003 B/call** (fixes 1+3 are on the common dispatch path, so the default
   `IGrainBehavior` path benefits too — its residual is the Part-A DI bucket). Throughput on the
   activation-scoped path ~+65% in the 6 s window. Clean Release build, 0 warnings. All 44
   scheduling-semantics/stuck-detector tests pass deterministically; the remaining full-suite failures
   are pre-existing wall-clock timing flakes (verified to fail identically with the spin **disabled**,
   so the spin does not worsen them).

   The last **~33 B/call** was a `ConcurrentStack<int>.Node` from `_idleWorkers.Push(workerIndex)` on
   each shard-empty transition. **DONE (2026-07-13):** replaced the `ConcurrentStack<int>` idle registry
   with a lock-free `long[]` **bitmask** (`_idleBits`; bit i = worker i idle). `RegisterIdle` is one
   `Interlocked.Or` (allocation-free, idempotent — one bit per worker vs the stack's possible duplicate
   entries); `TryClaimIdleWorker` scans words and claims the lowest set bit via per-word CAS (retrying a
   lost race). The `Interlocked.Or`/`CompareExchange` are full barriers, so the wake-race happens-before
   argument is identical to the stack's; the multi-word layout covers >64-core machines. The
   `Node[System.Int32]` type is now **absent** from the allocation profile. Correctness: 48
   scheduling/mailbox/stuck tests + 550/550 unit + 38/38 integration (incl. the #167 grain-to-grain
   fan-in wake path) all green; tens of millions of park/wake cycles across the profiler runs completed
   without a lost wake (which would stall throughput).

   **Regime nuance discovered while measuring the bitmask.** The activation-scoped residual is
   **bimodal**, governed by whether the spin-before-park catches the next item:
   - **Spin-catch regime** (quiet/fast box, round-trip < spin window ≈ sub-µs): the worker never parks;
     residual ≈ **1–2 B/call** (true zero — the bitmask removed the last idle-registration byte).
   - **Park regime** (loaded box, round-trip ≳ 1 µs > spin window): the worker parks on
     `SemaphoreSlim.WaitAsync(ct)`, whose slow path allocates ~200 B (`AsyncStateMachineBox` +
     `CancellationPromise` + `TaskNode` + `CompletionActionInvoker`). The bitmask still saves its 33 B
     here (park machinery ~200 B, vs ~233 B with the old stack), but the park machinery dominates.

   So the spin-before-park's near-zero is real but **load-sensitive**: it eliminates the park (and its
   ~200 B) only when the next message arrives inside the spin window. The remaining frontier is the park
   mechanism itself — either a longer/adaptive spin (CPU-vs-alloc tradeoff), or a pooled/token-free wake
   primitive replacing `SemaphoreSlim.WaitAsync(ct)` (its `CancellationPromise` + `AsyncStateMachineBox`
   are the bulk), or ArenaScheduler's wake design. That is a scheduler-park change, tracked separately.

   **Slice (c) (payload slabs) is moot** — the `MailboxWorkItem<T>` boxing it targeted is already
   <2 B/call. The MPSC ring / `MessageHandle` design is retained below for reference but is not being
   built: the Channel is not the allocator.

   **Park primitive — DONE (2026-07-13).** The park-regime ~200 B was eliminated by replacing the
   per-worker `SemaphoreSlim(0, int.MaxValue)` with **`WorkerParkSignal`** — a single-consumer /
   multi-producer async auto-reset event backed by a reusable `ManualResetValueTaskSourceCore<bool>`
   (`src/Quark.Runtime/WorkerParkSignal.cs`). A park now allocates **nothing**: the worker's async
   state-machine box already exists for the life of `RunWorkerAsync` (allocated once on first
   suspension, reused every park), and `WaitAsync()` hands it a bare `ValueTask` backed by the reusable
   source. The wait is **token-free** (dropping `SemaphoreSlim.WaitAsync(ct)`'s per-park
   `CancellationPromise<bool>` + registration node); `DisposeAsync` `Set()`s every worker on shutdown so
   each observes the cancelled token and exits, mirroring `ArenaScheduler`.

   A three-state field (`Idle`/`Signaled`/`Waiting`) mutated only by CAS mediates every producer/consumer
   transition, so the value-task core is touched by at most one thread at a time. A **boolean** auto-reset
   event is a sound replacement for the permit-counting semaphore because the scheduler's wake is
   *edge-triggered*: after one wake a worker re-sweeps every shard until a sweep is empty, so a single
   signal drains arbitrarily many ready activations — collapsing concurrent `Set()`s only elides redundant
   no-op sweeps, it can never strand work. Verified: 20 M multi-producer/consumer cycles through a faithful
   standalone model of the exact wake protocol (register-idle → claim-bit → `Set`) with zero lost wakes.

   Measured (`AllocByTypeProfiler`, workstation GC, **park regime** — the loaded-box case that previously
   cost ~200 B): activation-scoped **~200 → 1.3–1.9 B/call**; the only residual entries are the pooled
   `MailboxWorkItem<T>` (~1.5 B) and a stray `object[]` (~0.3 B). The park machinery is entirely gone from
   the profile in both regimes.

   **Latent dispose-hang uncovered and fixed (same session).** Swapping the primitive surfaced a
   pre-existing bug: `ActivationScheduler.ScheduleAsync` (since the `Channel<GrainActivation>` →
   `ConcurrentQueue` rewrite) no longer rejected work after shutdown. During `ServiceProvider` disposal the
   scheduler is disposed *before* `GrainActivationTable`, so when the table then disposes each activation,
   `GrainActivation.DisposeAsync` posts its `OnDeactivate` turn through `ScheduleAsync` and awaits the
   drain — but the drain workers have already exited, so the enqueue sat undrained and the poster hung
   forever. The old `Channel` ready queue threw `ChannelClosedException` here, which drove the activation's
   inline-drain fallback (`DrainDirectlyAndDeactivateAsync`); the `ConcurrentQueue` has no closed state, so
   the fallback became dead code. It stayed invisible only because the departing `SemaphoreSlim` park
   primitive *disposed its semaphores*, so the post-shutdown `Release()` threw `ObjectDisposedException` for
   an unrelated reason and accidentally drove the same fallback. `WorkerParkSignal.Set()` has nothing to
   dispose, removing that accident. **Fix:** `ScheduleAsync` now throws `ObjectDisposedException` when
   `_cts.IsCancellationRequested` (a single hot-path volatile read, only ever true during shutdown),
   restoring the reject-on-shutdown contract explicitly. `ScheduleAsync` is `async`, so the throw surfaces
   as a faulted `ValueTask` — `PostCoreAsync`'s `await` propagates it to the fallback, and `Deactivate()`'s
   `OnlyOnFaulted` continuation absorbs it. Locked in by `ActivationSchedulerShutdownRejectsScheduleTests`
   (2 tests; both fail — the second by hanging to a 10 s timeout — with the guard removed). Correctness:
   scheduling-semantics 45/45, Fault 9/9, Integration 38/38, unit 549–550/550 (the 1–2 residual failures
   are the documented wall-clock timing flakes that shift across runs and pass in isolation). Clean Release
   build, 0 warnings.

## Open questions

- **Reentrant + activation state:** do we require `IActivationBehavior` reentrant grains to opt into
  their own synchronization, or forbid mutable fields on reentrant activation behaviors outright
  (harder QRK0023 = error)?
- **`IActivationMemory<T>` on an `IActivationBehavior`:** allowed (redundant with fields) or
  discouraged? Leaning: allowed, since durable (`IPersistentActivationMemory<T>`) and managed
  (`IManagedActivationMemory<T>`) variants are still useful there.
- **Payload slab sizing / per-type pool caps** — needs a follow-up once Part B (a)/(b) land and we
  can measure descriptor churn under load.
- **`ArenaScheduler` reject-on-shutdown (opt-in path):** `ArenaScheduler.ScheduleAsync` has the same
  latent "silently accepts work after shutdown" gap the default scheduler had, currently masked the same
  accidental way (it disposes its per-worker semaphores, so a post-shutdown `Release()` throws
  `ObjectDisposedException` and drives the activation's inline-drain fallback). It should get the same
  explicit guard — but its `ScheduleAsync` returns synchronously, so the guard must be
  `return ValueTask.FromException(...)` (not `throw`) to keep `Deactivate()`'s
  `ValueTask vt = ScheduleAsync(...)` assignment from throwing into a `void` caller. Deferred: ArenaV2 is
  opt-in and out of scope for the park-primitive change.
