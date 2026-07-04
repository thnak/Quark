# Blueprint: Stateless-worker pool policy (scheduler Phase 6)

**Parent spec:** `docs/superpowers/specs/2026-07-03-single-node-activation-scheduler-design.md` (§12, §16 Phase 6)
**Date:** 2026-07-03
**Status:** Design blueprint — ready for issue breakdown. No code in this document.
**Compatibility tier:** mixed —
- `[StatelessWorker(maxLocalWorkers)]` attribute stays **drop-in** (unchanged Orleans surface).
- Multiplicity semantics (multiple concurrent worker activations per logical grain) become a **minor-change** internally: same Orleans concept, Quark-native implementation via synthetic activation identity.
- The `QueueCapacity` / `OverloadMode` admission knobs are **Quark-native** (Orleans `StatelessWorker` has no queue-policy surface).

---

## 1. Problem statement

`[StatelessWorker]` is resolved by `AttributePlacementStrategyResolver` into a
`StatelessWorkerPlacement`, but that strategy only influences *silo selection* (prefer-local).
It never produces more than one activation. `GrainActivationTable` keys strictly by `GrainId`
(`ConcurrentDictionary<GrainId, Lazy<Task<GrainActivation>>>`), and `GrainId` equality is
`(GrainType, string Key)`. So a `[StatelessWorker]` grain today gets **exactly one** activation
per key — identical to a normal grain. Phase 6 must add engine-managed worker multiplicity with
four bounds (`MaxLocalActivations`, `MaxConcurrentExecutions`, `QueueCapacity`, `OverloadMode`)
**without** rewriting `GrainActivationTable`, `LocalGrainCallInvoker`, the scheduler, or placement.

---

## 2. Current call path (as-is), verified against source

For a local grain call to logical `GrainId L = (Type, key)`:

1. `LocalGrainCallInvoker.InvokeAsync` / `InvokeVoidAsync` (`src/Quark.Runtime/LocalGrainCallInvoker.cs:78,146`)
   - `TryRouteRemote(L)` / `TryPlaceRemote(L)` decide remote vs local. **If we reach the local
     path, `GetOrActivateAsync` is called** — so pool selection can hook in *after* remote routing.
2. `GetOrActivateAsync(L)` (`:312`) → `_activationTable.GetOrCreateAsync(L, () => CreateActivationAsync(L, ct))`.
3. `CreateActivationAsync(L)` (`:326`):
   - resolves behavior `Type` via `_typeRegistry.TryGetGrainClass(L.Type, out behaviorType)`,
   - reads `[Reentrant]`, constructs `new GrainActivation(L, L.Type, isReentrant, …)`,
   - posts the activation barrier (`RunActivationAsync` + `MarkActive`),
   - `SetOnDeactivated(() => { _activationTable.Remove(L); dedupStore?.EvictGrain(L); … })` (`:360`),
   - `_directory.TryRegister(L, _siloAddress, out _)`.
4. Back in the invoker: `activation.PostAsync(work)` (`:116` / `:184`) enqueues the call into the
   activation mailbox → `PostCoreAsync` → `_scheduler.ScheduleAsync(this)`
   (`src/Quark.Runtime/GrainActivation.cs:559`).
5. `ActivationScheduler` (`src/Quark.Runtime/ActivationScheduler.cs:59`) is a **silo-wide singleton**,
   grain-type-agnostic. It caps `SchedulerMaxConcurrentActivations` across *all* activations,
   drains one non-reentrant turn per activation (single-turn invariant), applies drain budget /
   ready-queue overload. It has no per-grain-type awareness.

Key facts that make the design cheap:
- **`GrainId` is just `(GrainType, string Key)`** (`src/Quark.Core.Abstractions/Identity/GrainId.cs`).
  A distinct key string ⇒ a distinct `GrainId` ⇒ a distinct table entry ⇒ a distinct
  `GrainActivation`. This is the lever: *synthetic worker keys give us N activations for free.*
- **Activation memory is owned per-activation-instance, not keyed by GrainId.**
  `GrainActivation.GetOrCreateHolder<T>()` (`GrainActivation.cs:164`) stores holders in an instance
  `_memoryBag`; `ActivationMemoryAccessor` reads off `IActivationShellAccessor.Shell` (the specific
  worker activation). So per-worker `IActivationMemory<T>` isolation is automatic — see §7.
- The activation-creation logic (directory registration, diagnostics, `OnDeactivated`) all lives in
  `CreateActivationAsync` and is keyed off whatever `GrainId` it is handed. Handing it a synthetic
  id reuses all of it unchanged.

---

## 3. Proposed design

### 3.1 Core idea — synthetic worker activation identity

A logical stateless-worker call to `L = (Type, key)` is routed to one of up to *N* **worker
activations** with **synthetic GrainIds**:

```
W_i = (Type, key ‖ SENTINEL ‖ i)      i ∈ [0, MaxLocalActivations)
```

- `SENTINEL` is a reserved delimiter that cannot appear in a normal user key (recommend the ASCII
  Unit Separator ``; document that keys containing it are reserved for the runtime).
- `W_i` are **silo-local and invisible to callers**. The proxy still holds `L`; `GetGrain<T>(key)`
  still resolves to `L`. Only the invoker's local dispatch step swaps `L → W_i`.
- Each `W_i` is an ordinary `GrainActivation` in the existing `GrainActivationTable` — no table
  changes, no `ActivationId` concept needed.

### 3.2 Pool = per-logical-GrainId slot array + execution gate

One `StatelessWorkerPool` object per **logical `GrainId`** (see Open Question OQ1 for the
per-key vs per-type question). A pool owns:

- a fixed array of `MaxLocalActivations` **slots**, slot `i` ↔ stable synthetic id `W_i`;
- a per-slot busy/free flag (an `int[]` used with `Interlocked`, or a free-slot bitmap);
- an execution gate `SemaphoreSlim(MaxConcurrentExecutions)`;
- a bounded **waiter count** (`Interlocked int _waiters`) capped by `QueueCapacity`.

**Acquire (per call):**
1. Admission:
   - if `OverloadMode == RejectWhenFull` and `QueueCapacity > 0` and `_waiters >= QueueCapacity`
     ⇒ throw `SchedulerOverloadException` (reuse the Phase-4 type).
   - else `Interlocked.Increment(_waiters)`.
2. `await _executionGate.WaitAsync(ct)` (blocks under `Wait` mode = backpressure);
   then `Interlocked.Decrement(_waiters)`.
3. Claim the **lowest free slot** `i` (deterministic; keeps low ordinals hot so light load uses
   few live activations — see OQ3 for alternatives). A free slot is guaranteed to exist because
   busy slots ≤ permits-in-use ≤ `MaxConcurrentExecutions ≤ MaxLocalActivations`.
4. Return a `StatelessWorkerLease` carrying `WorkerId = W_i` and the slot index.

**Release (`Lease.Dispose`):** mark slot `i` free, `_executionGate.Release()`.

The pool never creates activations itself — it only picks a synthetic id. The invoker then calls
its *existing* `GetOrActivateAsync(W_i)`, which lazily activates `W_i` through the unchanged
table/scheduler path. Idempotent: if `W_i` already exists it is reused; if idle-collected, it is
re-activated transparently.

### 3.3 Interaction with `IActivationScheduler` — pool lives ABOVE the scheduler (recommended)

**The scheduler needs no pool awareness.** Each worker activation is an ordinary, non-reentrant
scheduler client. Correctness holds without scheduler changes:

- **Single-turn per worker:** each `W_i` is a normal non-reentrant activation; the scheduler already
  guarantees one turn at a time per activation.
- **`MaxConcurrentExecutions`:** enforced by the pool's `SemaphoreSlim` *before* `PostAsync`. The
  pool holds the permit for the whole `await PostAsync` duration and releases it in the lease's
  `finally`, so at most `MaxConcurrentExecutions` workers of this pool have in-flight work at once.
- **`SchedulerMaxConcurrentActivations`** (silo-wide) still applies on top, unchanged.
- **Fairness / drain budget / ready-queue overload:** unchanged; they operate per worker activation.

This matches parent-spec §12 line 294 ("scheduler APIs should allow a future policy layer to choose
which worker activation receives work") — the policy layer chooses `W_i`; the scheduler stays
generic. The alternative (teaching `ActivationScheduler` about pool groups) is rejected: it couples
a generic component to a niche policy and duplicates admission control the pool already does.

### 3.4 Where it hooks in `LocalGrainCallInvoker`

Exactly one place in each of `InvokeAsync` / `InvokeVoidAsync`, between the (unchanged) remote-routing
block and `GetOrActivateAsync`:

```
GrainId target = grainId;
StatelessWorkerLease lease = default;
bool pooled = _statelessWorkerRouter.TryGetPolicy(grainId.Type, out StatelessWorkerPoolPolicy policy);
if (pooled)
{
    lease = await _statelessWorkerRouter.AcquireAsync(grainId, policy, cancellationToken);
    target = lease.WorkerId;                    // synthetic W_i
}
try
{
    GrainActivation activation = await GetOrActivateAsync(target, cancellationToken);
    await activation.PostAsync(work);           // existing body unchanged
    …diagnostics keyed on logical `grainId` (caller-facing), unchanged…
}
finally
{
    if (pooled) lease.Dispose();                // free slot + release execution permit
}
```

`InvokeObserverAsync` is untouched (observers are not grains).

Diagnostics note: `OnInvocationStart/End` keep the **logical** `grainId` (caller identity), while
`OnGrainActivating/Activated`, drain, and mailbox events naturally carry the **synthetic** `W_i`
(worker identity) — desirable, since it lets operators see per-worker drains.

### 3.5 Deactivation / idle-collection interaction

`GrainIdleCollector` (`src/Quark.Runtime/GrainIdleCollector.cs:42`) iterates
`GetActiveActivations()` — which includes worker activations — and calls
`activation.Deactivate(IdleTimeout)`. That runs the worker's `OnDeactivated` callback
(`GrainActivation.cs:602`), which currently only does `_activationTable.Remove(W_i)`.

The pool must be notified so its slot is marked free and (if the whole pool is empty) the pool entry
is dropped. Because `SetOnDeactivated` takes a single `Func<Task>`, the invoker's
`CreateActivationAsync` must **compose** a pool-eviction step into the callback when the id is a
worker id:

```
activation.SetOnDeactivated(() =>
{
    _activationTable.Remove(target);
    dedupStore?.EvictGrain(target);
    _statelessWorkerRouter.OnWorkerDeactivated(target);   // NEW: free slot, drop empty pool
    return Task.CompletedTask;
});
```

`OnWorkerDeactivated` parses `W_i → (L, i)`, marks slot `i` free (it may already be free — idle
collection can only fire when the slot is not in-flight, but be defensive), and removes the pool
object when all slots are free and no activations remain. Since slot ids are *stable ordinals*, an
idle-collected worker simply re-activates on the next acquire; no count drift.

---

## 4. Exact files/types to add or change

### New files (all in `Quark.Runtime`, internal — this is silo-side policy)

| File | Type(s) | Responsibility |
|---|---|---|
| `src/Quark.Runtime/StatelessWorker/StatelessWorkerPoolPolicy.cs` | `readonly record struct StatelessWorkerPoolPolicy(int MaxLocalActivations, int MaxConcurrentExecutions, int QueueCapacity, SchedulerOverloadMode OverloadMode)` | Resolved per grain type; immutable. |
| `src/Quark.Runtime/StatelessWorker/StatelessWorkerLease.cs` | `readonly struct StatelessWorkerLease : IDisposable` with `GrainId WorkerId`, `void Dispose()` | Handed to the invoker; `Dispose` frees the slot + releases the gate. |
| `src/Quark.Runtime/StatelessWorker/StatelessWorkerPool.cs` | `internal sealed class StatelessWorkerPool` | Per-logical-GrainId slot array, execution gate, waiter bound; `AcquireAsync`, slot free/claim, `MarkWorkerDeactivated(int ordinal)`, `IsEmpty`. |
| `src/Quark.Runtime/StatelessWorker/StatelessWorkerRouter.cs` | `internal sealed class StatelessWorkerRouter` | Singleton. `bool TryGetPolicy(GrainType, out policy)` (attribute+options, cached); `ValueTask<StatelessWorkerLease> AcquireAsync(GrainId logical, StatelessWorkerPoolPolicy, CancellationToken)`; `void OnWorkerDeactivated(GrainId worker)`; owns `ConcurrentDictionary<GrainId, StatelessWorkerPool>` keyed by logical id; owns the synthetic-key `SENTINEL` encode/decode helpers `TryEncode(GrainId logical, int ordinal, out GrainId worker)` / `TryDecode(GrainId worker, out GrainId logical, out int ordinal)`. |

Signatures (blueprint only — no bodies):

```csharp
internal readonly record struct StatelessWorkerPoolPolicy(
    int MaxLocalActivations,
    int MaxConcurrentExecutions,
    int QueueCapacity,
    SchedulerOverloadMode OverloadMode);

internal readonly struct StatelessWorkerLease : IDisposable
{
    public GrainId WorkerId { get; }
    public void Dispose();          // frees slot + releases execution permit; no-op on default
}

internal sealed class StatelessWorkerRouter
{
    public bool TryGetPolicy(GrainType type, out StatelessWorkerPoolPolicy policy);
    public ValueTask<StatelessWorkerLease> AcquireAsync(
        GrainId logicalId, StatelessWorkerPoolPolicy policy, CancellationToken ct);
    public void OnWorkerDeactivated(GrainId workerId);
    public bool IsWorkerId(GrainId id);                         // fast SENTINEL check
    public bool TryDecode(GrainId workerId, out GrainId logicalId, out int ordinal);
}
```

### Modified files

| File | Change |
|---|---|
| `src/Quark.Runtime/LocalGrainCallInvoker.cs` | Add `StatelessWorkerRouter? _statelessWorkerRouter` ctor param (nullable, last positional to preserve existing call sites where possible). Insert the acquire/lease/release wrapper (§3.4) into `InvokeAsync` **and** `InvokeVoidAsync`. In `CreateActivationAsync`, compose `OnWorkerDeactivated(target)` into `SetOnDeactivated` when `_statelessWorkerRouter?.IsWorkerId(target) == true`. Detect the policy once per call via `TryGetPolicy` (needs the behavior `Type`; reuse `_typeRegistry.TryGetGrainClass`). |
| `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs` | `TryAddSingleton<StatelessWorkerRouter>()`; add it to the `LocalGrainCallInvoker` factory (`:77`). |
| `src/Quark.Runtime/SiloToSiloServiceCollectionExtensions.cs` | Add the router arg to the second `new LocalGrainCallInvoker(...)` site (`:42`, the `"silo-terminal"` keyed invoker) so both construction paths compile. |
| `src/Quark.Runtime/SiloRuntimeOptions.cs` | Add pool defaults (§5). |

### Reused, unchanged

- `SchedulerOverloadMode`, `SchedulerOverloadException` (Phase 4) — reused verbatim for pool overload.
- `GrainActivationTable`, `GrainActivation`, `ActivationScheduler`, `AttributePlacementStrategyResolver`,
  `StatelessWorkerAttribute`, `StatelessWorkerPlacement` — **no changes**.

### Tests to add (mirror parent-spec §15 item 12)

`tests/Quark.Tests.Unit/SchedulingSemantics/StatelessWorkerPoolTests.cs`:
- `MaxConcurrentExecutions` cap: burst N calls, prove ≤ MCE run concurrently.
- `MaxLocalActivations` cap: prove ≤ MLA distinct worker activations created (assert via
  `GrainActivationTable.Count` / a diagnostic listener counting `OnGrainActivating`).
- Overload `RejectWhenFull`: saturate gate + fill `QueueCapacity`, prove `SchedulerOverloadException`.
- Overload `Wait`: prove excess calls block then complete FIFO-ish (best-effort ordering).
- Per-worker activation-memory isolation: two concurrent calls land on different workers and see
  independent `IActivationMemory<T>` (§7).
- Idle-collect a worker, then a new call re-activates the same ordinal cleanly (no drift).

---

## 5. Options / defaults (`SiloRuntimeOptions`)

Follow the Phase-4 convention (defaults preserve prior behavior; explicit floors). Recommended:

```csharp
// Default worker count when [StatelessWorker] is declared with maxLocalWorkers = -1.
public int StatelessWorkerDefaultMaxLocalActivations { get; set; } = Environment.ProcessorCount; // floor 1
// Default waiter queue bound; 0 = unbounded (preserves "just backpressure" behavior).
public int StatelessWorkerQueueCapacity { get; set; }            // default 0
// Policy when the waiter queue is full. Reuses the Phase-4 enum.
public SchedulerOverloadMode StatelessWorkerOverloadMode { get; set; } = SchedulerOverloadMode.Wait;
```

Policy resolution (in `StatelessWorkerRouter.TryGetPolicy`, cached per `GrainType`):
- `MaxLocalActivations` = attribute `MaxLocalWorkers` if `>= 1`, else
  `StatelessWorkerDefaultMaxLocalActivations` (floor 1).
- `MaxConcurrentExecutions` = **defaults to `MaxLocalActivations`** (see §6 for why they coincide in
  the single-turn model). No attribute surface for it yet (Quark-native; add later if needed).
- `QueueCapacity` / `OverloadMode` from options above.

`Environment.ProcessorCount` for the default matches both Orleans' `StatelessWorker` default and the
Phase-4 `SchedulerMaxConcurrentActivations` default, so it is the consistent house choice.

---

## 6. Semantic finding: `MaxLocalActivations` ≈ `MaxConcurrentExecutions` in a single-turn model

Because every worker activation is **non-reentrant / single-turn** and the recommended router hands
each in-flight call a *dedicated* free slot (one call per worker at a time), the number of *live*
worker activations never exceeds the number of *concurrently executing* ones. With lowest-free-slot
routing, `MaxLocalActivations > MaxConcurrentExecutions` yields **no extra live workers** — the
higher ordinals are never claimed. The two knobs only diverge under a *different* policy, e.g.:
- keeping idle "warm" workers alive to avoid activation cost (retention policy), or
- allowing per-worker mailbox depth > 1 so `MaxLocalActivations` workers exist while only
  `MaxConcurrentExecutions` drain at once (reintroduces scheduler-level queueing).

Recommendation: ship v1 with `MaxConcurrentExecutions = MaxLocalActivations` (the two collapse, as
in Orleans), expose `MaxLocalActivations` as the single primary knob, and keep
`MaxConcurrentExecutions` in the internal policy record so a later retention/mailbox policy can make
them diverge without an API change. This is flagged as OQ2.

---

## 7. Per-worker activation memory (spec §12 bullet 4, non-goal §3)

**The distinct-synthetic-GrainId approach solves this for free for in-memory state:**

- `IActivationMemory<T>` / `IEagerActivationMemory<T>` / `IManagedActivationMemory<T>` resolve their
  `StateHolder<T>` / holder off `IActivationShellAccessor.Shell` — i.e. the *specific worker
  activation instance* (`GrainActivation._memoryBag`, not a GrainId-keyed global map). Two calls on
  two different workers therefore see **independent** state automatically. No extra work needed.

**Durable state needs a documented caveat (not new code):**

- `IPersistentActivationMemory<T>` and `[PersistentState]` write through `IGrainStorage` keyed by the
  activation's `GrainId` — which for a worker is the **synthetic** `W_i`. Consequences:
  1. State is per-worker (matches spec §12: "per worker activation, not logical grain state").
  2. The storage key embeds the SENTINEL + ordinal and is **not stable** across restarts (ordinal
     assignment is load-dependent), so durable state on a stateless worker is effectively scratch.
- This aligns with parent-spec non-goal §3 ("Do not make stateless workers stateful logical
  grains"). **Recommendation:** document loudly, and (follow-up, not Phase 6) add a QRK-series
  analyzer warning when a `[StatelessWorker]` behavior also declares `[PersistentState]` /
  `IPersistentActivationMemory<T>` / `JournaledGrain`. See OQ4.

---

## 8. Open questions (need a decision before implementation)

**OQ1 — Pool granularity: per logical GrainId, or per grain Type?**
Orleans stateless workers are typically addressed with a single well-known key, so per-`GrainId`
(the blueprint's choice) reproduces Orleans multiplicity and lets distinct keys have independent
pools. But if Quark intends `[StatelessWorker]` to mean "type-wide fungible workers ignoring the
key", the pool should key by `GrainType` and ignore the caller key entirely (callers' keys would
then be meaningless). **Recommendation: per logical GrainId.** Blocks slot-id encoding decision.

**OQ2 — Do we expose `MaxConcurrentExecutions` at all in v1?**
Per §6 it collapses into `MaxLocalActivations` under the single-turn/one-call-per-worker model.
Options: (a) keep it internal, default = MLA (recommended); (b) surface it now on a Quark-native
options/attribute even though it is a no-op until a retention policy exists. Recommendation: (a).

**OQ3 — Worker selection policy among free slots.**
Lowest-free-ordinal (recommended: minimizes live activations under light load, deterministic),
vs round-robin (even wear, more activations stay warm), vs random. Only matters if OQ2 keeps
MCE < MLA meaningful. Recommendation: lowest-free-ordinal.

**OQ4 — Durable state on stateless workers: allow, warn, or forbid?**
Blueprint allows it with a documented per-worker/unstable-key caveat. Alternative: emit an analyzer
warning (new QRK id) or hard-fail activation. Recommendation: allow + document now, analyzer later.

**OQ5 — Synthetic key sentinel + reserved-key contract.**
Confirm the `` (Unit Separator) sentinel and document that user grain keys must not contain it
for stateless-worker types (or make `TryDecode` robust to false positives by also checking the type
is a registered stateless-worker type). Recommendation: `` + type check in `IsWorkerId`.

**OQ6 — Do worker activations count toward the silo `MaxActivations` cap?**
They are real table entries, so today they would. That is arguably correct (they consume resources)
but means a bursting stateless-worker type can trip `GrainActivationLimitExceededException`.
Recommendation: let them count; document. Optionally exclude them later.

**OQ7 — Reentrant + StatelessWorker combination.**
`[Reentrant][StatelessWorker]`: a worker is single-turn unless also reentrant. With the gate holding
one permit per in-flight call and one call per worker, reentrancy inside a worker is moot for pool
accounting. Confirm we simply honor `[Reentrant]` per worker as usual. Recommendation: yes, no
special-casing.

---

## 9. Suggested implementation order (safe top-to-bottom, no circular deps)

1. **Options** — add the three `StatelessWorker*` properties to `SiloRuntimeOptions`. (Leaf change.)
2. **Policy + identity types** — `StatelessWorkerPoolPolicy`, and the SENTINEL encode/decode helpers
   + `StatelessWorkerLease` skeleton. Pure value types, no dependencies. Unit-test encode/decode
   round-trip and rejection of user keys containing the sentinel.
3. **`StatelessWorkerPool`** — slot array, execution gate, waiter bound, `AcquireAsync`, free/claim,
   `MarkWorkerDeactivated`, `IsEmpty`. Unit-testable in isolation with a fake "PostAsync" delay.
4. **`StatelessWorkerRouter`** — pool dictionary keyed by logical id, `TryGetPolicy`
   (attribute + options, cached), `AcquireAsync`, `OnWorkerDeactivated`, `IsWorkerId`, `TryDecode`.
5. **DI registration** — `TryAddSingleton<StatelessWorkerRouter>()` in
   `RuntimeServiceCollectionExtensions`; thread it into both `LocalGrainCallInvoker` factory sites
   (`RuntimeServiceCollectionExtensions` and `SiloToSiloServiceCollectionExtensions`).
6. **Invoker integration** — add the ctor param; wrap `InvokeAsync` / `InvokeVoidAsync` with
   acquire/lease/release; compose `OnWorkerDeactivated` into `SetOnDeactivated` in
   `CreateActivationAsync`. This is the only behavioral change to existing hot-path code.
7. **Tests** — `StatelessWorkerPoolTests` per §4; keep all existing `SchedulingSemantics` tests green
   (they use non-stateless-worker grains, so the router's `TryGetPolicy` returns false and the path
   is unchanged for them).
8. **Docs** — update `FEATURES.md` (stateless-worker parity), the persistence caveat in the
   `quark-persistence` skill / wiki `Persistence`, and add a stress/fairness note.

Steps 1–4 are independently testable before any hot-path code changes; steps 5–6 are the only edits
to shipping runtime paths and are guarded so non-stateless-worker grains take the identical old path.
