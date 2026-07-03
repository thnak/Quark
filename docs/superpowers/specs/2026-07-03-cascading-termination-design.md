# Design: Cascading termination for parent/child grain relationships

**Issue:** #120
**Date:** 2026-07-03
**Status:** Draft — ready for implementation
**Foundation-of:** #94 (workflows as a first-class primitive)
**Coordinates-with:** #126 (silo-to-silo transport), #37 (`GrainCancellationToken`), #124 (idempotency keys — coordination point only)

---

## 1. Problem statement

Quark grains are **flat**. Every activation is fully independent: there is no notion of "grains this
activation spawned" that should be cleaned up alongside it. Terminating a grain that fanned out to child
grains/sub-tasks leaves the children running, orphaned. This is the single most-requested pattern across
the durable-execution ecosystem (Temporal, DurableTask Azure/durabletask#446, Durable Functions
Azure/azure-functions-durable-extension#506 at 48👍/40💬 — the highest-engagement issue in the research
pass), and it is the runtime primitive that #94 (workflows) will need before a workflow-grain can define
what happens to its sub-workflows when the parent is terminated.

This spec designs cascading termination as a **general-purpose runtime primitive** — an opt-in parent/child
activation relationship plus best-effort termination propagation — that stands on its own for any
parent/child grain pattern. It deliberately does **not** design workflow semantics (§2 non-goals); it names
exactly the seam #94 will build on, the same way the #126 transport spec named #56/#60 as coordination
points without designing their content.

### What the code actually does today (verified against live source)

- **`GrainActivation`** (the shell, `src/Quark.Runtime/GrainActivation.cs`) holds `GrainId`, `GrainType`,
  the root `IServiceProvider`, a `Channel<MailboxWorkItem>` mailbox, a `_memoryBag`
  (`ConcurrentDictionary<Type, object>` of state/managed/eager holders), a `_timers` list, and lifecycle
  status. **There is no field, holder, or table that records another activation.** Confirmed: zero
  parent/child tracking.
- **`GrainActivationTable`** (`GrainActivationTable.cs`) is a flat
  `ConcurrentDictionary<GrainId, Lazy<Task<GrainActivation>>>`. Lookup/create/remove are keyed solely by
  `GrainId`; there is no adjacency or ownership structure. It exposes `TryGetActivation(grainId, out ...)`
  and a `TryDeactivateAsync(grainId)` that is annotated **`// TODO did not called anywhere`** — a
  ready-made local-termination hook that currently has no caller.
- **Deactivation today** (`GrainActivation.Deactivate(DeactivationReason)` → `RunDeactivationAsync`):
  1. `Deactivate` guards on status, sets `_status = Deactivating`, stores `_pendingDeactivationReason`,
     and posts `RunPendingDeactivationAsync` as a **fire-and-forget mailbox item** (`_queue.Writer.TryWrite`).
     Because it goes through the mailbox, **any currently-executing or already-queued call completes
     first** — deactivation drains, it never pre-empts.
  2. `RunDeactivationAsync(reason)`: `OnGrainDeactivating` diagnostic → `DisposeTimers()` →
     `OnDeactivateAsync(reason, CancellationToken.None)` on the behavior if it implements
     `IActivationLifecycle` (resolved from a **fresh scope** via `RunLifecycleHookAsync`) →
     `DisposeManagedHoldersAsync()` → `_status = Inactive` → `_queue.Writer.TryComplete()` → deactivation
     diagnostics/metrics.
  3. A `ContinuationFunction` runs `_onDeactivated` after the processing loop ends; `LocalGrainCallInvoker`
     sets that callback to `_activationTable.Remove(grainId)` (`LocalGrainCallInvoker.cs:304-308`).
  - **There is no hard-cancel path.** Deactivation is cooperative and mailbox-ordered. There is no existing
    mechanism to abort a running behavior.
- **`DeactivationReason`** (`src/Quark.Core.Abstractions/Grains/DeactivationReason.cs`) is a **sealed
  class**, not an enum — four static well-known instances (`IdleTimeout`, `ShuttingDown`,
  `ApplicationRequested`, `Force`) plus a public `ctor(string description, Exception? = null)`. Adding a
  new well-known reason and a boolean marker is a purely **additive** change, no enum churn.
- **`ICallContext`** (`src/Quark.Core.Abstractions/Hosting/ICallContext.cs`) exposes **only** `GrainId`.
  The callee has **no way to learn who called it** — there is no caller identity on the per-call context.
  Implicit parent inference (record parent = caller) is therefore **not available today** and would require
  threading caller `GrainId` through the whole invoke path. (Confirmed against `CallContext` /
  `ICallContextSetter.Set(GrainId)`.)
- **`IGrainContext`** is stale/unwired (project memory #78; the live per-call context is `ICallContext`).
  Behaviors that need the shell reach it via `IActivationShellAccessor.Shell` (a `GrainActivation`), which
  is the mechanism `RegisterTimer`, eager memory, and the Realm sample already use.
- **No cross-grain deactivation API exists.** A behavior can request only its *own* deactivation (via the
  shell). There is no supported way for grain A to deactivate grain B, locally or remotely.

### How #126 changes the picture (read before designing the remote leg)

`docs/superpowers/specs/2026-07-03-silo-to-silo-transport-design.md` introduces real cross-process grain
calls: a parent and its children may now live on **different silos**. The relevant reusable machinery it
establishes:

- `MessageEnvelope` / `MessageType` / `MessageDispatcher` / `SiloMessagePump` — the wire + dispatch path.
- `ISiloRouter.TryGetInvoker(SiloAddress)` → a `SiloCallInvoker` dialing a peer over a pooled
  `SiloPeerConnection`; peer lifecycle owned by `PeerConnectionManager`, driven by membership.
- The per-silo `InMemoryGrainDirectory` names the owning silo for an activated grain; **routing is by
  `GrainId` + the receiving silo's local decision** — grain calls do not carry a `SiloAddress` in the
  payload.
- The `x-quark-hop` header + local-terminal-invoker discipline that prevents forward loops.
- Its explicit residual limit: single-activation is guaranteed **only for `[HashBasedPlacement]`** until a
  distributed directory lands; per-process directories can be stale.

This spec's remote termination leg **reuses that path verbatim** (a new one-way control `MessageType`,
routed by the child `GrainId` through the same directory/router), and inherits the same residual limit
(§8). It does **not** invent a parallel routing mechanism.

---

## 2. Goals / Non-goals

### Goals

- An **opt-in, explicit** parent/child relationship: a behavior declares that a grain it references is its
  child, so the runtime can cascade termination to it.
- **Cascading termination**: when a parent activation deactivates for an *intentional* reason, each attached
  child is terminated too — **recursively** down the tree (a child cascades to its own children).
- **Reuse the existing cooperative deactivation semantics** for children: a child is deactivated via the
  same mailbox-draining `Deactivate(reason)` path it would follow for any deactivation. No hard/abortive
  kill is introduced. A child observes the cascade through the **existing** `OnDeactivateAsync(reason)`
  hook (reason = a new `ParentTerminated`) — its last chance to flush state.
- **Cross-silo cascade** consistent with #126: a child on a remote silo is terminated by routing a new
  one-way `TerminateRequest` control frame through the same directory/router/dispatch path a grain call
  uses. The parent stores only child `GrainId`s — never a `SiloAddress`.
- **Best-effort, non-blocking failure semantics**: a parent's own termination never blocks on an
  unreachable child. Cascade is fire-and-forget with idempotent delivery; a lost cascade leaves the child
  independent, which is benign (§8).
- **Amnesia by default**: the child set lives on the shell (in-memory), lost on the parent's own
  deactivation. Durable parent/child trees are the parent's responsibility (persist child IDs, re-attach in
  `OnActivateAsync`) — the primitive provides the hook, not the durability.
- AOT/trim-clean: additive well-known reason, plain `HashSet`/`ConcurrentDictionary` for the child set, a
  fixed-shape `GrainId` control frame (existing codecs), explicit DI registration, no reflection.

### Non-goals (explicit, load-bearing)

- **Full workflow semantics — #94.** Checkpointing, step-level transactions, replay, `IWorkflowGrain`,
  transactional "terminate parent + children atomically", and any *guaranteed exactly-once / durable*
  cascade that must survive a parent-silo crash are **deliberately out of scope**. This primitive is
  best-effort while both parent and child are live/reachable; making cascade a durable distributed
  guarantee is workflow-shaped and belongs to #94. This spec names the seam (§9) and stops.
- **Akka-style mandatory supervision hierarchy.** Quark's position (wiki `Lifecycle-and-Failure-Semantics`,
  §"Supervision") is that failure containment is an *engine invariant*, not a per-parent supervision
  strategy. This design adds an **opt-in relationship**, not a mandatory tree, and adds **no** supervision
  directives (`Restart`/`Resume`/`Escalate` have no analogue here). Restart-on-failure remains the existing
  reactivation-on-next-call behavior.
- **Hard/abortive termination of a running child call.** Termination drains the child's mailbox like any
  deactivation. Optionally *combining* cascade with #37 cancellation to shorten the drain is a named
  enhancement (§6), not a default, and never a forced thread abort.
- **Implicit parent inference from call context.** Not offered in v1 — the callee has no caller identity
  today (§1), and implicit ownership is ambiguous for a grain legitimately referenced by many callers.
  Ownership must be a deliberate declaration (§3, recommendation).
- **Cascade on passive deactivation.** Idle collection and silo shutdown do **not** cascade (§5) — cascade
  fires only for intentional termination reasons.
- **Automatic parent-death detection** that triggers cascade without an explicit `Deactivate`. The runtime
  does not watch for a parent silo dying and then reap its children cluster-wide (that is durable-cascade /
  #94 territory).
- **A distributed grain directory.** Cross-silo cascade inherits #126's residual limit; it does not fix it.
- **Reverse (child→parent) links / orphan notification.** v1 tracks children-on-parent only (§4).

---

## 3. How the relationship is established — recommendation: **explicit attach**

**Recommendation: explicit registration**, via a new Quark-native scoped abstraction the behavior injects,
`IActivationChildren`. The parent obtains a child reference exactly as today (`GetGrain<T>(key)`), then
explicitly declares the link:

```csharp
public sealed class OrderBehavior : IGrainBehavior, IOrderGrain, IActivationLifecycle
{
    private readonly IActivationChildren _children;
    private readonly IGrainFactory _factory;

    public OrderBehavior(IActivationChildren children, IGrainFactory factory)
        => (_children, _factory) = (children, factory);

    public async Task StartFulfilmentAsync()
    {
        var shipment = _factory.GetGrain<IShipmentGrain>(Guid.NewGuid());
        _children.Attach(((IGrain)shipment).GetGrainId());   // declare ownership
        await shipment.BeginAsync();
    }
}
```

**Why explicit, not implicit** (concrete justification):

1. **The callee has no caller identity today.** `ICallContext` exposes only `GrainId` (§1). Implicit
   inference would require threading the caller's `GrainId` through `IGrainCallInvoker` +
   `MessageDispatcher` + the wire — a large, hot-path change that #37 is already contending with. Building
   cascade on a capability that does not exist is the wrong dependency direction.
2. **Ownership is ambiguous under sharing.** A single grain is routinely referenced by many callers; the
   *first* caller is not necessarily its owner. Cascade deleting a grain because an unrelated caller
   happened to reference it first would be a correctness footgun. Ownership is a semantic assertion the
   author must make.
3. **It matches the house style.** Relationships that live on the shell (timers, managed/eager memory) are
   all declared explicitly against the shell. `Attach` is the same shape.

Implicit inference is left as a possible future convenience *if and when* caller identity lands on
`ICallContext` (coordinate with #37's scope-initializer work); it is not blocking and not v1.

### Per-child termination mode

`Attach` takes an optional mode so a parent can track a child without cascading to it (e.g. hand-off /
shared ownership):

```csharp
public enum ChildTerminationMode
{
    Cascade = 0,   // terminate this child when the parent terminates intentionally (default)
    Orphan  = 1,   // track the link but never cascade — explicit opt-out for this child
}
```

---

## 4. Where the relationship is tracked — shell-local, amnesiac by default

The child set lives on the **shell** (`GrainActivation`), in a new `ChildRegistry` held in the existing
`_memoryBag` (same lifetime class as timers and activation memory), exposed to behaviors through the
scoped `IActivationChildren` accessor — exactly mirroring how `ActivationMemoryAccessor<T>` projects a
`StateHolder<T>` from the shell.

```csharp
namespace Quark.Runtime;

// Shell-owned. Survives across calls of the same activation; lost on deactivation.
internal sealed class ChildRegistry
{
    // GrainId → mode. ConcurrentDictionary: Attach/Detach may race with a mailbox-ordered cascade read.
    private readonly ConcurrentDictionary<GrainId, ChildTerminationMode> _children = new();
    public void Attach(GrainId child, ChildTerminationMode mode) => _children[child] = mode;
    public bool Detach(GrainId child) => _children.TryRemove(child, out _);
    public IReadOnlyCollection<GrainId> Snapshot(ChildTerminationMode mode) => /* keys where value == mode */;
    public bool IsEmpty => _children.IsEmpty;
}
```

**Persistence decision: amnesiac by default, and that is the correct default.**

- The child set is **in-memory only**. If the parent deactivates (for any reason) and later reactivates, it
  starts with **no children** — the same amnesia timers and `IActivationMemory<T>` already have. A fresh
  parent activation does not know about children spawned before its last deactivation.
- Rationale: (a) durable child-set tracking invites *stale/orphan* entries — a child that independently
  deactivated or migrated would leave a dangling ID that a later cascade routes into the void; (b) making
  the relationship durable is exactly the workflow-durability problem #94 owns; solving it here would force
  a checkpoint/reconcile design this issue is scoped out of.
- **Durable trees are the parent's job, with a provided hook.** A parent that needs children to survive its
  own idle-collection persists their `GrainId`s in its own `IPersistentActivationMemory<T>` /
  `[PersistentState]` and re-`Attach`es them in `OnActivateAsync`. The primitive stays ephemeral; the
  author opts into durability. (This is the precise seam #94 will formalize — see §9.)

---

## 5. What cascading termination does to a child — exact semantics

### 5.1 Trigger: intentional deactivation only

Cascade fires **only** when the parent deactivates for an *intentional termination* reason — never on
passive deactivation. Encode this on `DeactivationReason` (additive):

```csharp
public sealed class DeactivationReason
{
    // existing: IdleTimeout, ShuttingDown, ApplicationRequested, Force
    public static readonly DeactivationReason ParentTerminated = new("ParentTerminated", cascades: true);

    // NEW: does this reason cascade to attached children?
    public bool CascadesToChildren { get; }

    // ctor gains an optional cascades flag (default false → source-compatible).
    public DeactivationReason(string description, Exception? exception = null, bool cascades = false) { ... }
}
```

| Reason | `CascadesToChildren` | Why |
|---|---|---|
| `ParentTerminated` (new) | **true** | The reason a child is itself terminated → recurses down the tree. |
| `Force` | **true** | Administrative/migration eviction is intentional termination of this logical grain. |
| `ApplicationRequested` | **true** | Explicit user `Deactivate` — the author intends to tear this grain down. |
| `IdleTimeout` | **false** | Memory optimization, **not** logical termination. An idle parent must **not** kill live children; it will simply reactivate with amnesia on the next call. |
| `ShuttingDown` | **false** | Silo shutdown deactivates **every** grain independently; each child already receives its own `ShuttingDown`. Cascading would double-fire and race the child's own teardown. |

This table is the single most consequential semantic in the design: **idle collection of a parent leaves
its children alive.**

### 5.2 What happens to the child

When the parent's `RunDeactivationAsync(reason)` runs with `reason.CascadesToChildren == true`, then
**after** the parent's own `OnDeactivateAsync` hook completes (so the parent may `Detach`/flush first), the
shell reads the `Cascade`-mode child snapshot and, for each child, calls the runtime terminator
fire-and-forget (§6). Each child then goes through the **ordinary cooperative deactivation path**:

- The child's shell runs `Deactivate(DeactivationReason.ParentTerminated)`. This posts teardown as the
  **next mailbox item**, so **a call the child is currently executing, and any already-queued calls,
  complete first** — the child drains, it is never pre-empted.
- If the child implements `IActivationLifecycle`, it observes the cascade through the **existing**
  `OnDeactivateAsync(reason, ct)` hook with `reason == ParentTerminated`. **No new hook is required** — the
  graceful signal is the reason value on the hook the child already has. This is the child's last chance to
  flush.
- Because `ParentTerminated.CascadesToChildren == true`, the child's own attached (Cascade-mode) children
  are cascaded in turn — the tree unwinds depth-first via each shell's mailbox.

### 5.3 A child's uncommitted state

No new state semantics — cascade reuses exactly what any deactivation does:

- `IActivationMemory<T>` (ephemeral) and `IManagedActivationMemory<T>` resources: **lost/disposed**, as on
  any deactivation.
- `IPersistentActivationMemory<T>`, `[PersistentState]`, `JournaledGrain`: retain **only what was already
  explicitly written**. `OnDeactivateAsync(ParentTerminated)` is the child's opportunity to `SaveAsync()`
  if it wants last-moment durability. The runtime does **not** auto-flush and does **not** roll back — a
  child mid-mutation that has not written is simply lost, identical to an idle/forced deactivation.
- A child under an in-flight `[Transaction]`: the transaction's own commit/abort/timeout governs atomicity
  (same stance as #37 §Q7). Cascade does **not** force-abort a 2PC; the queued teardown runs after the
  transactional call drains.

### 5.4 Cycle / re-entrancy guard

An attached-child graph can contain a cycle (A attaches B, B attaches A). `Deactivate` is already
idempotent — it no-ops unless status is `Active`/`Activating` — so a second cascade reaching an
already-`Deactivating` grain stops naturally. That existing guard is sufficient to terminate recursion; no
separate visited-set is needed for the local path. (The remote path is likewise idempotent — §6.2.)

---

## 6. Termination propagation — the runtime terminator

Cascade fan-out goes through a single silo-singleton runtime service that resolves local-vs-remote, so the
shell does not need routing knowledge:

```csharp
namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Best-effort, fire-and-forget termination of a grain activation by GrainId, wherever it lives.
///     Local: activation-table lookup + cooperative Deactivate(reason).
///     Remote: routes a one-way TerminateRequest frame via the same directory/router path a grain call uses.
///     Never blocks the caller; never throws for an unreachable target.
/// </summary>
public interface IActivationTerminator
{
    void Terminate(GrainId target, DeactivationReason reason);
}
```

Implementation (`Quark.Runtime`, `DefaultActivationTerminator`, registered by `AddQuarkRuntime()`):

### 6.1 Local leg

`_activationTable.TryGetActivation(target, out var act)` → `act.Deactivate(reason)`. If the target is not
locally present, fall through to the remote leg. (This finally gives `GrainActivationTable`'s dormant
`TryDeactivateAsync`/`TryGetActivation` a real caller.) No await — `Deactivate` is void and posts to the
child's own mailbox.

### 6.2 Remote leg (consistent with #126)

If the target is not local, consult the **same** grain directory / `ISiloRouter` path a normal call uses:

- Directory hit for a remote owner → send a one-way `MessageType.TerminateRequest` frame over the peer's
  `SiloPeerConnection` (via the router's `SiloCallInvoker` / a one-way send). **Payload = the target
  `GrainId` (existing `GrainId` codec: type + key) + a 1-byte reason discriminator + an optional 16-byte
  dedup id (§10).** No `SiloAddress` on the wire — routing is by `GrainId`, exactly as #126 established.
- The receiving silo handles `TerminateRequest` **inline on the read loop** — the same discipline #37 uses
  for `CancelRequest` — *not* through the awaited grain-dispatch path. `SiloMessagePump` /
  `GatewayMessagePump` peek the `MessageType`, decode the `GrainId`, and call the local
  `IActivationTerminator.Terminate(target, ParentTerminated)` synchronously (which runs its own §6.1 local
  leg). One-way, no response frame, `CorrelationId` unused (`-1`, like `StreamPush`/`CancelRequest`).
- **Directory miss** (child not activated / owner unknown) → **no-op**. There is nothing to terminate; a
  not-activated grain has no state to tear down. Safe.
- **Loop discipline:** a `TerminateRequest` is terminal at the receiving silo — it is handled locally and
  never re-forwarded from the inline handler (the handler calls the *local* leg only). This mirrors #126's
  hop-marker intent without needing the marker, because the frame is not a routable grain call.

**Idempotency of remote terminate:** `Terminate` is idempotent by construction — `Deactivate` no-ops on a
non-Active grain, so a duplicated/retried `TerminateRequest` for an already-terminating or absent grain is
harmless. This is what makes fire-and-forget safe (§8) and is the property #124 would strengthen (§10).

### 6.3 Optional fast-drain via #37 (named, not default)

To shorten a child's drain when it is mid-call, `Terminate` *may* first fire the child's
`GrainCancellationTokenRuntime.Cancel(...)` for its in-flight call (if #37 has landed) so a cooperative
child returns quickly, then deactivates. This is an **enhancement toggle**, off by default; cancellation
and termination stay distinct (§7). Not required for v1.

---

## 7. Boundary: termination vs. cancellation (#37)

They are related but distinct and must not be conflated:

| | `GrainCancellationToken` (#37) | Cascading termination (#120) |
|---|---|---|
| **Target** | A single in-flight **call**. | A whole **activation** (and its subtree). |
| **Effect** | Cooperative signal *within* a call; the behavior observes a token and returns early. | The activation drains its mailbox, runs `OnDeactivateAsync`, and is removed. |
| **Outlives the call?** | No — scoped to one invocation. | Yes — the grain is gone until re-activated by a future call. |
| **Wire frame** | `MessageType.CancelRequest` (Guid token id). | `MessageType.TerminateRequest` (GrainId + reason). |
| **Composition** | — | *May optionally* trigger a child's cancel first to shorten the drain (§6.3). |

Cancelling a call does not deactivate a grain; terminating a grain does not, by itself, cancel an unrelated
in-flight call — it drains it. Keeping the two frames and runtimes separate keeps each semantics clean.

---

## 8. Failure semantics — best-effort, non-blocking

**A parent's own termination never blocks on a child.** The fan-out is fire-and-forget:

- The parent shell posts `Terminate(child, ParentTerminated)` for each Cascade-mode child and **does not
  await** the outcome. The parent's teardown proceeds and completes regardless.
- **Rationale:** blocking a parent's deactivation on a possibly-partitioned or slow child would risk a
  stuck grain / hung silo shutdown (`StuckGrainDetector` territory) — the exact pathology #126 avoided by
  faulting fast rather than hanging. A parent must be able to die promptly.
- **Unreachable child (silo down / partition):** the remote send fails or the directory has no reachable
  owner → the terminate is **dropped**, a diagnostic is emitted (`OnChildTerminationFailed`, §11), and the
  parent continues. **A lost cascade is benign:** the child either (a) is reaped by its own silo's
  lifecycle when that silo restarts, or (b) reactivates later elsewhere with **amnesia** — it no longer
  holds any attachment and is simply an independent grain. There is no dangling supervision state to leak.
- **Delivery guarantee: at-most-once, best-effort.** Default is a single fire-and-forget send with no
  retry. `SiloRuntimeOptions` MAY expose a bounded `ChildTerminationRetry` (small count, no unbounded
  backoff) for operators who want more effort; retry is safe because `Terminate` is idempotent (§6.2). A
  **guaranteed** cascade (durable outbox / retry-until-acked / survives parent crash) is explicitly **not**
  provided — that is #94 (§9).

This mirrors #126's discipline exactly: fail-fast, emit a signal, defer the durable/retry policy.

---

## 9. Relationship to #94 (workflows) — the named seam, not its content

Per the issue, #120 is the *foundation* #94 needs, and #94 is genuinely unspecced. This spec provides the
**mechanism** and names precisely what #94 must add on top, without designing it:

- **What #120 gives #94:** an opt-in parent/child link (`IActivationChildren.Attach`), a well-known
  cascading reason (`ParentTerminated`), a runtime terminator that resolves local/remote and routes over
  #126, and best-effort recursive teardown with clean failure semantics.
- **What #94 must add (out of scope here):**
  1. **Durable child-set tracking** — persist the tree so a workflow-parent that crashes/reactivates
     re-owns its children. §4 provides the hook (persist IDs + re-`Attach` in `OnActivateAsync`); #94
     decides the checkpoint/reconcile model.
  2. **Guaranteed cascade** — retry-until-acked / durable outbox so termination is not best-effort. §8
     names this as the delta.
  3. **Step/checkpoint/replay semantics, `IWorkflowGrain`, cross-step transactionality** — none of which
     this primitive presumes.

**Honest open question surfaced, not guessed (per scoping guidance):** whether cascade should survive a
**parent silo crash** (parent dies without running `RunDeactivationAsync`, so no cascade ever fires) is a
*workflow-durability* question. Answering "yes" requires durable tree state + a reaper that detects the
dead parent and cascades on its behalf — which is #94's durability model, not a general runtime primitive.
This spec therefore **defers** it explicitly: v1 cascade fires only when the parent runs an intentional
deactivation. This is stated as an open question (§12-Q1) rather than guessed.

---

## 10. Coordination with #124 (idempotency keys) — note only

A `TerminateRequest` is a call-shaped message that a retry layer could duplicate. Correctness does **not**
depend on dedup — `Terminate`/`Deactivate` are idempotent (§6.2), so a duplicate terminate is harmless.
**Coordination point (not a redesign):** the frame reserves an optional 16-byte dedup-id slot; if #124
lands a general call-dedup key, the terminator should stamp it so a retried terminate dedups through the
same mechanism rather than a bespoke one. If #124 does not land, the reserved slot stays `default` and
idempotency-by-construction is sufficient. (Spec `2026-07-03-idempotency-key-design.md` does not exist yet;
this is a forward note, not a dependency — §12-Q4.)

---

## 11. Compatibility tier — **Quark-native** (with honest Akka comparison)

| Surface | Tier | Justification |
|---|---|---|
| `IActivationChildren` (`Attach`/`Detach`), `ChildTerminationMode` | **Quark-native** | No Orleans equivalent — Orleans grains are flat virtual actors; `DeactivateOnIdle` is self-only, there is no parent/child cascade API to drop in. |
| `IActivationTerminator`, `DefaultActivationTerminator` | **Quark-native** | New runtime service; no public Orleans analogue. |
| `DeactivationReason.ParentTerminated` + `CascadesToChildren` | **additive** | Reuses the existing extensible sealed-class reason type; new static + optional ctor flag, source-compatible. Not a new enum, not a breaking change. |
| `MessageType.TerminateRequest` | **additive / Quark-native** | New one-way control frame, same pattern as #37's `CancelRequest`. |

**Why not drop-in, and the Akka comparison** (the repo already has an Akka-mapping precedent in
`wiki/Lifecycle-and-Failure-Semantics.md`, which literally names #120):

- **Orleans** has no comparable primitive — there is nothing to be a drop-in *of*.
- **Akka.NET** is the closest analogue: stopping a parent actor **automatically** stops its entire child
  hierarchy, because Akka actors form a *mandatory* supervision tree. Quark's deliberate philosophical
  stance (wiki) is the **inverse**: no mandatory tree; failure containment is an engine invariant, and
  relationships are opt-in. So this design offers Akka's *stop-cascades-to-children* behavior as an
  **opt-in relationship** rather than an implicit structural guarantee, and pointedly does **not** import
  Akka's supervision *directives* (`Restart`/`Resume`/`Escalate`) — those have no home in the virtual-actor
  model (restart == reactivation-on-next-call). Honest trade: Akka gives you the tree for free but forces
  it on you; Quark keeps grains flat by default and lets you declare a cascade only where you mean it.

Update the wiki §"Supervision" row for `Escalate`/cascading to point at this shipped design instead of the
open issue.

---

## 12. AOT / trim safety

- **No reflection, no dynamic codegen.** The child set is a plain `ConcurrentDictionary<GrainId, ...>`; the
  terminator is straight control flow.
- **Wire frame is fixed-shape** — the existing `GrainId` codec (type string + key string) + a 1-byte reason
  discriminator + optional 16-byte Guid, written/read with `CodecWriter`/`CodecReader` primitives already
  proven AOT-clean. **No new serializable user type, no `ISerializable`** (would trip QRK0003), no
  polymorphic payload.
- **Explicit registration only** — `AddQuarkRuntime()` registers `IActivationTerminator` and the scoped
  `IActivationChildren` accessor (same pattern as `IActivationMemory<T>` accessors, emitted or hand-wired).
  No assembly scanning.
- The `DeactivationReason` change is a static field + an optional bool ctor parameter — trim-neutral.
- New code sits in `Quark.Runtime` / `Quark.Core.Abstractions` (`IsTrimmable=true`,
  `EnableAotAnalyzer=true` via `Directory.Build.props`); no `[RequiresUnreferencedCode]` /
  `[RequiresDynamicCode]` expected — any that appear signal design drift.
- **AOT smoke:** extend the existing `PublishAot=true` runtime smoke with a parent that attaches a child and
  cascades — must stay warning-free.

---

## 13. Testing strategy

Hand-write invokers/proxies per house style; use `TestCluster` for in-process, and #126's
`NetworkedTestCluster` for the cross-silo leg.

- **Unit — attach + local cascade:** parent attaches child; `parent.Deactivate(ApplicationRequested)` →
  child receives `OnDeactivateAsync(ParentTerminated)` and is removed from the activation table. Assert via
  a per-child deactivation counter + `table.Count`.
- **Unit — recursion:** A→B→C attached; terminating A tears down B then C (assert order/ancestry via
  counters).
- **Unit — cycle guard:** A attaches B, B attaches A; terminating A terminates both exactly once, no
  infinite loop (relies on `Deactivate` idempotency).
- **Unit — mode = Orphan:** an `Orphan`-mode child is tracked but **not** terminated when the parent
  terminates.
- **Unit — no cascade on passive reasons:** parent with an attached child is idle-collected
  (`IdleTimeout`) and separately `ShuttingDown` → the child is **not** cascaded (the headline "idle parent
  keeps live children" guarantee, §5.1).
- **Unit — amnesia:** attach child, terminate parent, reactivate parent → new parent activation reports zero
  children.
- **Unit — mailbox drain:** child is mid-call when cascade arrives → the in-flight call completes before
  `OnDeactivateAsync(ParentTerminated)` runs (assert ordering via a side-effect log).
- **Unit — Detach stops cascade:** attach then `Detach` → terminating the parent does not touch the ex-child.
- **Fault (`Quark.Tests.Fault`)** — child unreachable (inject a routing/send failure): parent still
  completes its own teardown (fire-and-forget, §8) and an `OnChildTerminationFailed` diagnostic is emitted.
- **Integration (cross-silo, `NetworkedTestCluster`)** — parent on silo A, `[HashBasedPlacement]` child on
  silo B; terminate the parent → `TerminateRequest` routes to B and the child on B deactivates. Assert a
  **duplicate** `TerminateRequest` is a harmless no-op (idempotency, §6.2).
- **AOT smoke** per §12.
- **Flaky-test caution (memory):** keep any timing-sensitive cascade/drain assertions tolerant with
  generous margins; prefer deterministic table-count/counters over sleep-based timing.

---

## 14. Implementation sequence (circular-dep-safe, top-to-bottom)

1. `src/Quark.Core.Abstractions/Grains/DeactivationReason.cs` — add `ParentTerminated` static +
   `CascadesToChildren` property + optional `cascades` ctor parameter (source-compatible). Set
   `Force`/`ApplicationRequested` cascades = true.
2. `src/Quark.Core.Abstractions/Hosting/ChildTerminationMode.cs` — new enum.
3. `src/Quark.Core.Abstractions/Hosting/IActivationChildren.cs` — new scoped accessor abstraction.
4. `src/Quark.Core.Abstractions/Hosting/IActivationTerminator.cs` — new runtime-terminator abstraction.
5. `src/Quark.Runtime/ChildRegistry.cs` — shell-owned child set (in `_memoryBag`).
6. `src/Quark.Runtime/GrainActivation.cs` — expose `GetOrCreateChildRegistry()`; in `RunDeactivationAsync`,
   after the parent `OnDeactivateAsync` hook, if `reason.CascadesToChildren`, resolve `IActivationTerminator`
   from `_root` and fire-and-forget `Terminate(child, ParentTerminated)` for each `Cascade`-mode child.
7. `src/Quark.Runtime/ActivationChildrenAccessor.cs` — scoped `IActivationChildren` projecting the shell's
   `ChildRegistry` (mirrors `ActivationMemoryAccessor<T>`).
8. `src/Quark.Runtime/DefaultActivationTerminator.cs` — local leg (`GrainActivationTable` lookup +
   `Deactivate`); remote leg gated on the directory/router being present (§6.2), else local-only/no-op.
9. `src/Quark.Transport.Abstractions/MessageType.cs` — add `TerminateRequest` (one-way).
10. `src/Quark.Runtime/SiloMessagePump.cs` + `GatewayMessagePump.cs` — inline `TerminateRequest` decode +
    `IActivationTerminator.Terminate(...)` on the read loop (mirror #37's `CancelRequest` handling).
11. `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs` (`AddQuarkRuntime`) — register
    `IActivationTerminator` (singleton) + scoped `IActivationChildren` accessor.
12. `src/Quark.Diagnostics.Abstractions` — add `OnChildTerminationFailed` event (no-op default) + optional
    `OnChildCascaded`.
13. (Optional) `BehaviorRegistrationGenerator` — emit the `IActivationChildren` scoped accessor registration
    alongside the existing memory-accessor registrations, so it is available without hand-wiring.
14. Tests per §13; extend `NetworkedTestCluster` usage for the cross-silo case.
15. Wiki: update `Lifecycle-and-Failure-Semantics.md` §Supervision (point the `Escalate`/cascade row at this
    design); add a "Parent/child cascade" section; `FEATURES.md` parity entry; note #94 as the durable
    follow-up.

No step references a forbidden package: abstractions carry the interfaces/enum; the runtime holds the
shell/terminator/pump changes; the remote leg uses only #126's already-runtime-side directory/router. The
client is untouched.

---

## 15. Open questions

1. **(Biggest / load-bearing) Parent-silo-crash cascade.** v1 cascade fires only when the parent runs an
   intentional `RunDeactivationAsync`. If a parent's **silo crashes**, no cascade ever fires and its
   children (possibly on other silos) become orphans until independently collected. Making cascade survive a
   parent crash needs durable tree state + a dead-parent reaper — **workflow-durability, #94**. **Recommend:**
   ship best-effort-while-live now; defer crash-cascade to #94; document the limit. *This is the deliberate
   scoping boundary the reviewer should confirm.*
2. **Cross-silo cascade correctness under #126's directory limit.** The remote leg routes by the per-process
   `InMemoryGrainDirectory`, which #126 guarantees only for `[HashBasedPlacement]`. Under non-deterministic
   placement or a stale directory, a `TerminateRequest` may route to the wrong/no silo → **silent cascade
   loss**. Benign by §8 (lost cascade = independent child), but worth stating in docs. Fully fixed only by
   the distributed-directory follow-up #126 already named. Confirm this inherited limit is acceptable for
   v1.
3. **`IActivationChildren` surface vs. shell-direct.** Offer the scoped `IActivationChildren` accessor
   (recommended — mirrors `IActivationMemory<T>`, keeps behaviors off a direct `Quark.Runtime` shell
   reference), or expose `Attach`/`Detach` directly on `GrainActivation` via `IActivationShellAccessor`
   (as timers/eager memory are today)? Recommend the accessor; confirm no objection to the extra abstraction.
4. **#124 dedup slot.** Reserve the optional 16-byte dedup id on `TerminateRequest` now (recommended,
   forward-compatible, idempotency already covers correctness), or add it only if/when #124 lands? Recommend
   reserving it; it is 16 zero bytes until used.
5. **Retry policy default.** Default fire-and-forget at-most-once (recommended), or a small bounded
   `ChildTerminationRetry` default > 0? Recommend at-most-once default with the option available, matching
   #126's fail-fast-then-defer-policy discipline.
6. **Implicit inference later.** If #37's scope-initializer work puts caller identity on `ICallContext`,
   should a future `AttachCaller()`/auto-attach convenience be offered? Out of scope for v1; note as a
   possible follow-up, not a commitment.

---

## 16. Dependencies & related work

- **#126 (silo-to-silo transport)** — provides the directory/router/dispatch path the remote termination
  leg reuses (§1, §6.2); this spec inherits its `[HashBasedPlacement]`-only cross-silo correctness limit
  (§12-Q2). Local cascade works without #126; the remote leg activates when it is present.
- **#37 (`GrainCancellationToken`)** — establishes the inline-control-frame pattern
  (`CancelRequest` on the read loop) this spec mirrors for `TerminateRequest`; §7 draws the
  cancellation-vs-termination boundary; §6.3 names an optional compose.
- **#124 (idempotency keys)** — forward coordination only (§10); no hard dependency.
- **#94 (workflows)** — the primary consumer. This spec is its foundation and names exactly what #94 must
  add (durable tree, guaranteed cascade, crash-cascade) without designing it (§9).
- **`wiki/Lifecycle-and-Failure-Semantics.md`** — the Akka-mapping precedent that already names #120; update
  its Supervision section to reference this design (§11, §14 step 15).
