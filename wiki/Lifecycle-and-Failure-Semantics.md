# Lifecycle and Failure Semantics

This page is the **engine contract**: what lives how long, and exactly what happens when things fail. Every statement here is derived from the current runtime source (file references inline) — where behavior is a known limitation rather than a designed guarantee, it says so and links the tracking issue.

If you are wondering "where is Quark's supervision story?", this page is it — see the [Akka.NET mapping](#supervision-how-this-maps-to-akkanet) at the end.

## The lifetime contract

| Thing | Created | Lives | Destroyed |
|---|---|---|---|
| **Activation shell** (`GrainActivation`) | First call to a grain identity (or implicit stream activation) | Across all calls to that identity | Idle collection, silo shutdown, or activation failure |
| **Per-call `IServiceScope`** | Start of each call (and once at activation) | One call | Disposed when the call completes — even if it throws |
| **Behavior instance** (POCO) | Resolved inside the per-call scope | One call | Dies with the scope |
| **`IActivationMemory<T>` / `IEagerActivationMemory<T>`** | First access / eagerly at activation | With the shell | Discarded at deactivation — never persisted |
| **`IManagedActivationMemory<T>`** | Lazy async `Init` on first `GetAsync()` | With the shell | `Destroy` callback awaited during deactivation |
| **`IPersistentActivationMemory<T>`** | `ReadStateAsync` at activation | Shell cache + `IGrainStorage` | Cache discarded at deactivation; storage survives |
| **`[PersistentState]` slot** | Re-read from storage **each call** | One call (handle); storage is durable | Handle dies with the scope |
| **`JournaledGrain` projection** | Replayed from `ILogStorage` at activation | With the shell | Projection discarded; event log survives |
| **Grain timers** | `RegisterGrainTimer` | With the shell | All disposed first thing in deactivation |
| **Reminders** | `RegisterOrUpdateReminderAsync` | **Beyond** the activation — durable in the reminder store | Only by explicit `UnregisterReminderAsync` |
| **Stream subscriptions** | `SubscribeAsync` | Until `UnsubscribeAsync` | Explicit unsubscribe (see [#101](https://github.com/thnak/Quark/issues/101) for the deactivation-timeout edge) |

The activation/deactivation sequence diagrams live in [Persistence § Lifecycle at a glance](Persistence#lifecycle-at-a-glance). Deactivation order, verified against `GrainActivation.RunDeactivationAsync`: **dispose timers → `OnDeactivateAsync` → destroy managed/eager holders → complete the mailbox → remove from table**.

## When a behavior method throws

The most important rule in the engine: **an exception is contained to the failing call.** Verified against `LocalGrainCallInvoker.InvokeAsync` (catch → diagnostics → rethrow) and `GrainActivation.RunLoopAsync` (catch → log → continue):

| Question | Answer |
|---|---|
| Does the caller see the exception? | **Yes.** In-process: the original exception, rethrown. Over TCP: a failure response — see [wire errors](#error-shape-at-the-tcp-boundary) below |
| Is the activation killed? | **No.** The shell stays active; nothing is torn down |
| Is activation memory preserved? | **Yes** — including mutations the method made *before* throwing. There is no automatic rollback |
| Does persistent state roll back? | **No.** `IPersistentActivationMemory<T>` keeps the in-memory mutation; storage only changes on your explicit `WriteStateAsync()`. Mutate-then-write, or write only after validation |
| Are queued calls still processed? | **Yes.** The mailbox loop catches, logs, and moves to the next item |
| Are timers still active? | **Yes** — and a throwing *timer callback* is logged (surfaced via `OnTimerFired` diagnostics) without stopping the timer |
| Can I observe it? | `OnInvocationEnd` carries the exception; `quark.grain.invocations.errors` counts it |

Design consequence: because the behavior instance is discarded after every call anyway, a throw can never leave *behavior* state corrupted — the only state that can be left half-mutated is what you put in engine-owned memory, which is why the guidance is to validate before mutating, or keep mutations idempotent.

**Repeated failures:** there is deliberately no automatic recycling yet — a grain that fails every call keeps failing and keeps answering. Poison-message quarantine / recycle-after-N-failures is designed in [#132](https://github.com/thnak/Quark/issues/132). Contract tests pinning all of the above: [#130](https://github.com/thnak/Quark/issues/130).

## When activation itself fails

If the behavior constructor, a `GrainScopeInitializer`, or `OnActivateAsync` throws:

1. The exception propagates to the caller that triggered activation.
2. The faulted activation is **removed from the activation table** (`LocalGrainCallInvoker` → `GrainActivationTable.RemoveIfFaulted`).
3. The **next call creates a fresh activation** — a partially-initialized activation is never reused.

This is the virtual-actor equivalent of a supervisor `Restart`, driven by the next incoming message rather than a parent. Constructor-level failures are additionally caught at **silo startup**: `BehaviorStartupValidator` constructs every registered behavior once and aborts startup on DI misconfiguration, so "missing registration" is a boot error, not a runtime surprise. (`OnActivateAsync` logic is not startup-validated — only the dependency graph is.)

## Mailbox, ordering, and backpressure

Verified against `GrainActivation` (`Channel<MailboxWorkItem>`, `SingleReader = true`):

- **Ordering:** strict FIFO per activation, one work item at a time. This serialization *is* the actor model's correctness guarantee — grain code never needs locks for its own state.
- **Capacity:** unbounded by default. Set `SiloRuntimeOptions.MailboxCapacity` to bound it, with two full-mailbox policies:
  - `MailboxFullMode.Wait` — callers asynchronously wait for space (backpressure propagates to callers);
  - `MailboxFullMode.RejectWhenFull` — enqueue fails fast with `MailboxFullException`.
  An unbounded mailbox under a hot producer is a memory risk — bound it for grains exposed to untrusted call rates.
- **Reentrancy:** `[Reentrant]` behaviors skip the queue and execute immediately — calls interleave, and your state must tolerate that. Timer callbacks respect `GrainTimerCreationOptions.Interleave` (default `false`: a tick is skipped rather than run concurrently with a pending one).
- **Cancellation:** caller cancellation tokens are **not** currently propagated into queued work — an in-flight call runs to completion. First-class call cancellation is [#37](https://github.com/thnak/Quark/issues/37); long-call heartbeating is [#97](https://github.com/thnak/Quark/issues/97).
- **Deactivation interplay:** deactivation is itself a mailbox item, so already-queued calls drain first; after it runs, the channel is completed and later calls go to a fresh activation.
- **Deadlock surface:** a grain awaiting itself (A→B→A non-reentrant) deadlocks, as in any single-mailbox actor system. `StuckGrainDetector` (`AddQuarkStuckGrainDetector()`) reports any work item running past `DiagnosticOptions.StuckThreshold` via `OnMailboxStuck`, and `OnMailboxStuckResolved` when it clears — it observes, it does not kill. A static analyzer for reentrancy deadlock patterns is [#117](https://github.com/thnak/Quark/issues/117).

Contract tests pinning scheduling semantics: [#131](https://github.com/thnak/Quark/issues/131).

## Error shape at the TCP boundary

Today, honestly: when a remote grain call fails, the silo returns `success = false` with `Exception.ToString()` as the error text (`MessageDispatcher`), and the TCP client rethrows it as an `InvalidOperationException` carrying that text (`TcpGatewayCallInvoker`). That means:

- The **message and server stack trace** reach the client; the **exception type does not** — you cannot `catch (MyDomainException)` across the gateway, and local vs remote calls diverge on failure.
- Sending stack traces to arbitrary clients is an information-disclosure concern.

Both halves are tracked as one wire-format decision: typed, `[GenerateSerializer]`-based exception envelopes in [#133](https://github.com/thnak/Quark/issues/133), fail-secure masking of unregistered exception details in [#57](https://github.com/thnak/Quark/issues/57). Until they land, treat remote failure handling as string-based and put machine-readable failure data in return types rather than exception types.

## Streams and observers on failure

- **Typed subscribers** (`IAsyncObserver<T>`): a throwing subscriber does not block the others — every subscriber receives the item, then the publisher gets an `AggregateException` of whatever failed. Delivery is at-most-once per publish; there is no retry or redelivery ([#63](https://github.com/thnak/Quark/issues/63) defines the delivery-mode/backpressure story, [#41](https://github.com/thnak/Quark/issues/41) adds durable recoverable streams).
- **Untyped subscribers**: currently inconsistent — the first throwing untyped observer halts delivery to the remaining untyped ones. Filed as a bug: [#134](https://github.com/thnak/Quark/issues/134).
- **Grain observers** (`IGrainObserver`): invocation results (success/failure) surface via `OnObserverInvoked` diagnostics; a dead client observer fails its invocation without affecting the grain.

## Transactions on failure

`[Transaction]` + `ITransactionalState<T>` today implement **best-effort 2PC**, and you should size your trust accordingly:

- Mutations inside a transaction go to a pending copy; commit writes pending state to storage per participant, sequentially.
- If a participant's commit throws, **subsequent participants are not committed, and already-committed participants are not compensated** — there is no durable transaction log or automatic recovery yet.
- Abort clears pending state (mutations are discarded — this rollback works).

Completing the 2PC protocol (prepare/commit/abort with a durable log) is [#62](https://github.com/thnak/Quark/issues/62); transactional delete semantics are [#102](https://github.com/thnak/Quark/issues/102). Until #62 lands, use transactions for single-storage-failure-domain coordination, keep operations idempotent, and do not treat multi-grain transactions as ACID across storage failures.

## What deactivation guarantees

Triggered by idle collection (`GrainIdleCollector`, enabled by setting `SiloRuntimeOptions.GrainCollectionAge`; disabled by default), silo shutdown, or activation failure. Guarantees, in order:

1. Queued work drains first (deactivation waits its turn in the mailbox).
2. All grain timers are disposed before any user hook runs.
3. `OnDeactivateAsync(reason, ct)` runs — a throw here is caught and logged, and cleanup continues.
4. Every `IManagedActivationMemory<T>.Destroy` callback is awaited — this is the guaranteed-cleanup home for buffers, handles, clients, and subscriptions.
5. The mailbox is completed (no new work) and the shell is removed from the table; the next call re-activates from durable state.

`DelayDeactivation(TimeSpan)` defers idle collection; graceful whole-silo drain with a configurable timeout is [#61](https://github.com/thnak/Quark/issues/61).

## Supervision: how this maps to Akka.NET

Quark has no parent/child actor tree; supervision decisions are made by the engine, uniformly, at two boundaries:

| Akka.NET directive | Quark equivalent |
|---|---|
| `Resume` (keep state, keep going) | The default for any behavior-method exception: activation and memory survive, mailbox continues, caller gets the error |
| `Restart` (fresh actor, replay identity) | Activation-failure handling: faulted activations are removed; the next call rebuilds from durable state. Manually forceable by letting the activation idle-collect, or (planned) failure-triggered recycle [#132](https://github.com/thnak/Quark/issues/132) |
| `Stop` | Deactivation (idle collection / shutdown) with the cleanup guarantees above |
| `Escalate` | No hierarchy to escalate through. The operator escalation path is diagnostics: error counters, `OnInvocationEnd` exceptions, `StuckGrainDetector`. Parent/child cascading termination is proposed in [#120](https://github.com/thnak/Quark/issues/120) |

The philosophical difference: in Akka, *you* write supervision strategies per parent; in Quark, failure containment is an engine invariant (calls are isolated, state is engine-owned, cleanup is guaranteed) and the remaining policy knobs — quarantine thresholds, recycle policies — are configuration, not code. Whether that trade suits you is a fair adoption criterion; [Why Quark](Why-Quark) treats it honestly.

## Known gaps, in one place

| Gap | Tracked |
|---|---|
| No poison-message quarantine / failure-rate recycle | [#132](https://github.com/thnak/Quark/issues/132) |
| Remote exceptions lose their type; stack traces cross the wire | [#133](https://github.com/thnak/Quark/issues/133), [#57](https://github.com/thnak/Quark/issues/57) |
| Untyped stream observer failure halts remaining untyped delivery | [#134](https://github.com/thnak/Quark/issues/134) |
| 2PC lacks durable log / compensation on partial commit | [#62](https://github.com/thnak/Quark/issues/62) |
| No caller-driven call cancellation | [#37](https://github.com/thnak/Quark/issues/37) |
| Failure/scheduling contracts documented but not yet pinned by tests | [#130](https://github.com/thnak/Quark/issues/130), [#131](https://github.com/thnak/Quark/issues/131) |
| Delivery guarantees (dedup, acks) not yet formalized | [#59](https://github.com/thnak/Quark/issues/59) |
| Mutable `static` state on behaviors not yet flagged by analyzers | [#129](https://github.com/thnak/Quark/issues/129) |

The umbrella for all of this is the v1.0 engine epic, [#128](https://github.com/thnak/Quark/issues/128).
