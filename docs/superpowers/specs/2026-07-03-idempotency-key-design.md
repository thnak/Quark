# Design: Idempotency-key support for at-most-once grain calls

**Issue:** #124
**Date:** 2026-07-03
**Status:** Draft — ready for implementation
**Related:** #59 (delivery-guarantee semantics + dedup + one-way acks — **overlap resolved in §4**), #128 pillar 4 (failure semantics), #126 (silo-to-silo transport — shares the wire-header mechanism), #56 (transport auth — principal propagation seam)

> Sibling spec sharing today's date: `2026-07-03-silo-to-silo-transport-design.md` (#126). Distinct filename; no collision.

---

## 1. Problem statement

A grain call over an unreliable transport can complete on the silo *after* the client has already
given up waiting (client-side timeout) and retried. Nothing in Quark today lets the silo recognise the
retry as a duplicate of a call it already executed, so a non-idempotent method (payment, transfer,
counter increment) runs twice. Retry safety is entirely the application's problem.

### What the code actually does today (verified against live source)

- **No stable call identity exists on the wire.** `GrainInvocationRequest`
  (`src/Quark.Runtime/GrainInvocationRequest.cs:7`) is `record (GrainId GrainId, uint MethodId,
  ReadOnlyMemory<byte> ArgumentPayload)` — no call-id, no idempotency field. It is hand-serialized by
  `GrainMessageSerializer.SerializeRequest`, **not** `[GenerateSerializer]`.
- **`MessageEnvelope.CorrelationId` is not a usable dedup key.** It is generated per-invoker-instance by
  `Interlocked.Increment(ref _nextCorrelationId)` in `TcpGatewayCallInvoker.SendAsync`
  (`src/Quark.Client.Tcp/TcpGatewayCallInvoker.cs:96`). A client **retry** calls `SendAsync` again →
  a **new** correlation id. A **reconnect** builds a fresh `TcpGatewayCallInvoker`/`TcpGatewayConnection`
  whose counter restarts at 0. So the correlation id identifies a *wire frame on one connection*, never a
  *logical call across retries*. **This is the precise reason #59's "dedup keyed by correlation id"
  proposal (see §4) does not actually stop a retry from double-executing.**
- **The server-side dispatch choke point exists and is shared.** `MessageDispatcher.DispatchRequestAsync`
  (`src/Quark.Runtime/MessageDispatcher.cs:51`) deserializes the request and invokes the local
  `IGrainCallInvoker`. Both the client→gateway path (`SiloMessagePump`/`GatewayMessagePump`) and the
  silo→silo forwarded path (#126's `SiloMessagePump`) funnel through this one method. It already copies
  `envelope.Headers` onto the response envelope (`:91`, `:103`) — so headers already round-trip.
- **`MessageHeaders`** (`src/Quark.Transport.Abstractions/MessageHeaders.cs`) is an `Ordinal`
  string→string map with `Set`/`Get`. #126 adds `x-quark-hop` to it. It is the natural, already-present
  carrier for a dedup key. `TcpGatewayCallInvoker` currently sets **no** headers on outbound requests
  (`:97-102`), so originating a header is additive.
- **`ICallContext`** (`src/Quark.Core.Abstractions/Hosting/ICallContext.cs`) exposes only `GrainId`,
  is registered Scoped, and is populated server-side from the `GrainId` via `ICallContextSetter`. It is a
  *server-side reconstruction*, so it cannot itself *carry* a client-supplied key across the wire — the
  key must ride the envelope and be surfaced *onto* `ICallContext` at the receiving end.
- **Activation lifetime bounds any per-activation store.** `GrainActivation` is the long-lived shell;
  `GrainIdleCollector` (`src/Quark.Runtime/GrainIdleCollector.cs`) deactivates idle grains
  (disabled by default, `GrainCollectionAge == TimeSpan.Zero`). Any per-activation dedup cache dies with
  the activation — cheap, but forgetful across deactivation (§5).
- **`[Transaction]` is metadata-only today.** `TransactionAttribute` docstring: *"In Phase 5 this is
  metadata only; auto-coordination middleware is deferred. Tests coordinate manually via
  ITransactionCoordinator."* `TransactionCoordinator` (`src/Quark.Transactions/TransactionCoordinator.cs`)
  drives commit/abort over registered writers via an `AsyncLocal<Guid>` tx id. No transaction middleware
  intercepts the call path. This shapes the answer in §6.

### Conclusion

The gap is real and confirmed: there is **no stable, caller-controllable identity for a logical grain
call**, so a retried call is indistinguishable from a first call. The fix is (a) a caller-stable
**idempotency key** that survives retries *and* reconnects, rides the existing header map, and (b) a
bounded **server-side dedup store**, consulted at the point of local execution, that returns the recorded
outcome instead of re-executing. Correlation-id dedup (#59) is a *different, weaker* mechanism that this
spec supersedes for the retry case (§4).

---

## 2. Goals / Non-goals

### Goals
- A **caller-supplied, opt-in idempotency key** (stable across retries and reconnects) attached per logical
  call, requiring **no change to generated grain-proxy method signatures** (preserves drop-in interface
  shapes).
- **At-most-once execution** of a keyed call within a bounded dedup window: the grain method body runs at
  most once per key; a retry replays the recorded outcome (success payload **or** the same error).
- **One dedup mechanism that serves both transport paths** — client→gateway and silo→silo forwarded
  (#126) — riding the same `MessageHeaders` map, with dedup performed only at the **terminal executing
  silo**, never at a forwarding silo.
- **Bounded, self-evicting store** (TTL + per-grain capacity), pruned on deactivation, with an explicit,
  named durability escalation for the small set of methods that need cross-deactivation / cross-crash
  correctness (§5).
- **Misuse detection:** a key reused with different arguments is rejected, not silently mis-served (§7).
- A crisp, decisive answer to the two open questions the issue itself poses: transaction interaction (§6)
  and key placement (§4.1).
- AOT/trim-clean: string header, byte-hash, explicit DI, no reflection, no `ISerializable`, no exception
  serialization (§9).

### Non-goals (explicit)
- **Framework-generated call-id on every request.** The issue floated this as the heavier alternative.
  Rejected: it imposes a dedup-store write on *every* call for a guarantee almost no method needs, and
  frames dedup as always-on rather than opt-in. Keys are opt-in per call.
- **Crash-consistent exactly-once.** True exactly-once requires the side effect and the dedup record to
  commit atomically (transactional outbox). This spec delivers *at-most-once within the dedup window under
  non-crash conditions* for the in-memory tier and *survives deactivation* for the durable tier; the
  residual crash gap (side effect committed, dedup record not) is documented (§5, §7), not closed here.
- **The delivered-guarantee documentation table and one-way acks** — that is #59's residual scope after
  this spec takes the dedup mechanism (§4). This spec provides the store #59's exit criterion depends on;
  it does not write the guarantee doc or the ack frame.
- **Automatic client-side retry.** This spec makes a retry *safe*; it does not *perform* retries. Retry
  policy (backoff, when to retry) is the caller's / a future policy layer's.
- **Idempotency for streams and reminders.** Those have their own at-least-once delivery semantics
  (#59 B5/B6); the reminder-delivery branch in `MessageDispatcher` is left untouched.
- **In-process direct-call dedup.** A direct in-process `LocalGrainCallInvoker` caller `await`s the actual
  result or exception — there is no ambiguous timeout, hence no duplicate. Dedup is a transport-path
  concern; the in-process direct path is unaffected and pays nothing.

---

## 3. Architecture overview

```
client sets key:  using var _ = QuarkRequestContext.WithIdempotencyKey("pay-42");
                  await grain.TransferAsync(...);          // proxy signature UNCHANGED
                        │
   TcpGatewayCallInvoker reads ambient key → stamps MessageEnvelope.Headers["x-quark-idem"]="pay-42"
                        ▼  TCP (retry/reconnect re-sends the SAME key)
   gateway silo A: SiloMessagePump → MessageDispatcher.DispatchRequestAsync
                        │
                        ├─ owner == A (local exec)  ─────────────┐
                        └─ owner == B → SiloCallInvoker forwards  │  (A does NOT dedup; copies
                             (copies x-quark-idem, adds x-quark-hop)  x-quark-idem onto forward)
                                        ▼                          │
                        silo B: MessageDispatcher (terminal) ◄─────┘
                                        ▼
                 ── DEDUP CHECKPOINT (terminal executing silo only) ──
                 IRequestDedupStore.TryBegin(grainId, key, argHash):
                   • HIT+Completed  → return recorded response bytes (NO re-exec)
                   • HIT+InFlight   → await the in-flight completion, return its bytes
                   • HIT+argHash≠   → return failed response: IdempotencyKeyConflict
                   • MISS           → execute via local invoker, record outcome, return
                                        ▼
                 GrainInvocationResponse ◄── back over the same correlation id
```

The dedup checkpoint sits **inside `DispatchRequestAsync`, on the branch that executes locally**. A
forwarding silo never reaches its own checkpoint for that grain (the call is routed onward before local
execution), so it never caches a response it did not produce. The terminal silo — whether it is the
gateway (owner == self) or the forwarded-to peer (`x-quark-hop` present) — is the single place execution
and therefore dedup happen.

---

## 4. #59 relationship — resolved decisively (merge the mechanism, split the docs)

**They are not orthogonal and #124 is not a clean subset — they overlap on exactly one artefact (the
bounded response cache) and are disjoint on everything else.** Precise split:

| Concern | Owner after this spec |
|---|---|
| **Stable idempotency key** (survives retry + reconnect), its wire placement, at-most-once *execution* semantics, misuse detection | **#124 (this spec)** |
| **The bounded per-activation response cache / dedup store mechanism** | **#124 delivers it**; #59 consumes it |
| Documented delivery-guarantee table (per message type) | **#59** |
| One-way `ack` frame so a client can *stop* retrying | **#59** |
| Correlation-id dedup (dedupe an on-wire frame re-send on one connection) | **superseded** — see below |

**Why #59's stated design is insufficient (verified, not asserted):** #59 proposes *"a per-grain
recent-response cache keyed by **correlation id**."* Correlation id is a per-connection counter
(`TcpGatewayCallInvoker.cs:96`); a retry increments it and a reconnect resets it, so keying on it
**cannot** match a retry to its original — the exact scenario #124 exists to solve. #59's correlation-id
key catches only a literal duplicate frame on a single live connection (a transport-level re-send), which
is a much narrower event. Therefore this spec **replaces the key** with the caller-stable idempotency key
while **keeping #59's cache idea**, and generalises the store so #59's own exit criterion (*"a retried
request executes the grain method once"*) is satisfied by this store keyed on `x-quark-idem`.

**Recommendation to the maintainer:** relabel #59 to *"delivery-guarantee documentation + one-way acks
(dedup mechanism delivered by #124)"* and mark its bullet 2 (correlation-id dedup) as superseded by this
spec. Do **not** implement a second, correlation-id-keyed cache — it would double the machinery and give a
strictly weaker guarantee. This spec's `IRequestDedupStore` is the one cache both issues share.

### 4.1 Where the key lives (API surface decision)

Three candidate carriers were considered; the generated-proxy constraint decides it.

| Option | Verdict |
|---|---|
| Extra method parameter on the grain method | **Rejected.** `GrainProxyGenerator` emits proxies from the *user's* interface signatures; adding a param would break drop-in Orleans interface shapes and force every method to carry it. |
| First-class field on `GrainInvocationRequest` | **Rejected.** Churns the hand-serialized wire record + `GrainMessageSerializer`; only *keyed* calls need it, so it wastes bytes on every call; and it does not exist on the in-process path or one-way/reminder envelopes that already carry headers for free. |
| **Ambient scoped `QuarkRequestContext.IdempotencyKey`**, read by the invoker, stamped into `MessageHeaders["x-quark-idem"]`, surfaced server-side on `ICallContext.IdempotencyKey` | **Chosen.** No proxy-signature change (preserves drop-in interfaces); flows through `async` naturally; reuses the existing header map (same mechanism as #126's `x-quark-hop`); works uniformly for request, one-way, and forwarded calls. Mirrors Orleans' `RequestContext` idiom, which Quark users already expect. |

The ambient carrier is **scoped** (a `using` disposes it) to prevent a key leaking into the *next* call on
the same async flow — the classic `RequestContext` footgun. See §7 for the leak-guard.

---

## 5. The dedup store — tier choice made explicit

The store is consulted at the terminal silo's dispatch checkpoint. Entry shape:

```
key = (GrainId, idempotencyKey)     value = { ulong argHash, State }
State = InFlight(Task<ReadOnlyMemory<byte>>) | Completed(ReadOnlyMemory<byte> responsePayload)
```

`responsePayload` is the already-serialized `GrainInvocationResponse` bytes — **including the failure
case** (`Success=false, Error=...`). A key is *consumed by execution regardless of outcome*: a method that
performs a side effect and then throws must **not** re-run on retry, so the thrown outcome is recorded and
replayed identically. Only the `Error` **string** is stored — no exception object is serialised (AOT-safe;
avoids `ISerializable`/QRK0003, §9).

**Concurrent duplicates** are handled by the `InFlight(Task)` state: the first arrival installs a TCS,
executes, then completes it; a duplicate that arrives while the first is still running awaits the same Task
and returns its bytes. This dedupes even reentrant grains (where the mailbox does not serialise) and
avoids a second execution racing the first.

### Tier decision: ship **per-activation in-memory**; name **durable** as opt-in escalation

| Tier | Survives deactivation? | Survives crash? | Cost | Verdict |
|---|---|---|---|---|
| **Per-activation in-memory** (bounded TTL + per-grain LRU cap; evicted when the activation deactivates) | **No** | No | Zero I/O; a dictionary hanging off activation lifetime | **Shipped default** |
| **Durable** (`IGrainStorage`-backed dedup record, written before ack) | Yes | Partly (crash gap remains, §7) | A storage write on the hot path per keyed call | **Opt-in escalation**, `IdempotencyOptions.Durability = Durable` |

**Chosen default: per-activation in-memory.** Rationale: the dominant real retry window is *seconds* — a
client times out and retries while the activation is still hot. Within that window the in-memory tier is
correct and free. It maps cleanly onto Quark's activation-lifetime model (the store evicts a grain's keys
on that grain's `SetOnDeactivated` callback, so `GrainIdleCollector`-driven deactivation prunes it
automatically) and needs no persistence dependency.

**Documented limitation (load-bearing):** if the activation **deactivates** between the original call and
the retry (idle collection, rebalancing, silo restart), the key is forgotten and the retry **re-executes**
— at-most-once degrades to at-least-once for that key. Methods that need exactly-once *across
deactivation* (payments) must opt into the **durable** tier, which writes the dedup record to
`IGrainStorage` keyed by `(GrainId, idempotencyKey)` before acking. The durable tier still has the
crash-between-side-effect-and-record gap (§7); genuine exactly-once needs the side effect and the dedup
record in one transaction (transactional outbox — out of scope, §2).

Placing the *authoritative* store behind an `IRequestDedupStore` seam (rather than literally on
`GrainActivation`) keeps `MessageDispatcher` decoupled from activation internals and lets the durable
implementation swap in without touching the dispatch code. The default implementation still keys eviction
to activation lifetime, so it *is* "per-activation" in every observable way.

---

## 6. Interaction with `[Transaction]` / 2PC — decisive answer: **dedup does not apply to transactional calls**

The issue asks whether idempotency keys should apply to `[Transaction]`/2PC calls. **They should not.**

- **The transaction is already the correctness boundary.** 2PC gives commit-once atomicity across writers
  via `TransactionCoordinator`. Layering a request-level idempotency window on top duplicates that
  guarantee and creates two sources of truth that can diverge.
- **Replaying a cached response would be actively wrong.** If a caller times out on a transactional call
  and retries, the *transaction* may have aborted (its correct failure mode). Returning a cached
  "success" — or even a cached abort — from a request-level dedup window masks the transaction's real,
  possibly newer, outcome and defeats the retry's purpose of re-driving the protocol. A retried
  transaction must re-enter the transaction machinery, not hit a response cache.
- **Today this is moot but must be future-proofed.** `[Transaction]` is metadata-only right now (no
  coordination middleware intercepts the call path — verified §1), so in practice transactional methods
  simply are not given idempotency keys yet. When transaction middleware lands (its own issue), it stamps
  an `x-quark-tx` marker on the envelope; the dedup checkpoint **skips any request carrying `x-quark-tx`**
  (bypass, execute normally). Reverse guard: `QuarkRequestContext.WithIdempotencyKey` is a no-op /
  logged-warning when the call resolves to a `[Transaction]` method, so a key set on a transactional call
  is ignored rather than silently caching.

**Rule, stated once:** idempotency keys govern *non-transactional* calls; transactional exactly-once is
delivered by the transaction protocol. The two mechanisms are mutually exclusive per call.

---

## 7. Failure & edge cases

| Case | Behaviour |
|---|---|
| **Retry with same key, different arguments** (caller bug: key reused for a semantically different call) | **Reject, do not trust the caller.** The store records a cheap non-cryptographic hash (FNV-1a/xxHash) of the argument payload with each entry. On a key hit whose incoming `argHash` differs, return a failed `GrainInvocationResponse` with a distinct `IdempotencyKeyConflict` error rather than replay the wrong cached result. Cheap (bytes are already in hand at the dispatcher) and catches a real, common client bug. Hash collision risk is negligible and only ever *fails safe* (rejects, never mis-serves). |
| **Store eviction races a legitimate late retry** (TTL expiry / LRU cap / deactivation before the retry arrives) | The retry re-executes: at-most-once degrades to at-least-once for that key. **Inherent** to any bounded dedup window (same as AWS/Stripe idempotency-key TTLs). Mitigation is configuration, not code: default TTL must be `>>` the client's retry timeout; document the relationship. The durable tier removes the deactivation trigger but not the TTL one. |
| **Concurrent duplicate while original in flight** | Second request awaits the `InFlight` Task and returns the same bytes — one execution (§5). |
| **Server crash mid-dedup-write** (in-memory tier) | The whole cache is lost with the activation; the grain re-activates with no memory; the retry re-executes. Fundamental limit of the in-memory tier; the durable tier narrows it to the classic gap: crash *after* the side effect commits but *before* the dedup record commits → still a re-execution. Closing that fully needs side-effect + dedup-record in one transaction (out of scope, §2). Stated plainly so no reader over-trusts the guarantee. |
| **Ambient key leaks to the next call** on the same async flow | `QuarkRequestContext.WithIdempotencyKey` returns an `IDisposable` scope that clears the `AsyncLocal` slot on dispose; the invoker also clears it after reading so a non-scoped misuse cannot bleed into an unrelated subsequent call. |
| **One-way request (`OneWayRequest`) with a key** | No response to cache; the store records a `Completed(empty)` marker so a re-send is suppressed (no second execution). The client cannot *observe* the suppression — learning delivery is #59's one-way `ack`, out of scope here. |
| **Key present but grain method not idempotent-safe to replay a stale success** | Out of the framework's knowledge; the contract is documented: a key means "run at most once and replay my recorded outcome." Semantic correctness of replaying that outcome is the method author's responsibility. |

---

## 8. API surface

### 8.1 Client-side key carrier (new, `Quark.Core.Abstractions.Hosting`)

```csharp
namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Ambient, async-flow-scoped carrier for per-call request metadata (idempotency key today).
///     Read by the outbound IGrainCallInvoker and stamped onto MessageEnvelope headers.
///     Quark-native; conceptually the minimal analogue of Orleans' RequestContext.
/// </summary>
public static class QuarkRequestContext
{
    /// <summary>The idempotency key for the current async flow, or null.</summary>
    public static string? IdempotencyKey { get; }

    /// <summary>
    ///     Sets the idempotency key for calls made inside the returned scope; the AsyncLocal slot is
    ///     cleared on Dispose. Opt-in and per logical call.
    /// </summary>
    public static IDisposable WithIdempotencyKey(string key);
}
```

### 8.2 Server-side surfacing (additive to existing interface)

```csharp
// ICallContext gains one nullable member — additive, no break.
public interface ICallContext
{
    GrainId GrainId { get; }
    string? IdempotencyKey { get; }   // NEW: the caller-supplied key for this call, or null
}
```

### 8.3 Dedup store seam (new, `Quark.Runtime`)

```csharp
namespace Quark.Runtime;

/// <summary>
///     Bounded, self-evicting store that gives a keyed grain call at-most-once execution within a
///     window. Consulted only at the terminal executing silo's dispatch checkpoint.
/// </summary>
public interface IRequestDedupStore
{
    /// <summary>
    ///     Begins (or joins) a keyed call. Returns a lease describing whether the caller must execute
    ///     or may replay a recorded outcome. argHash guards against key reuse with different arguments.
    /// </summary>
    ValueTask<DedupLease> TryBeginAsync(
        GrainId grainId, string idempotencyKey, ulong argHash, CancellationToken ct = default);

    /// <summary>Records the terminal outcome (success or failure bytes) for a lease that executed.</summary>
    void Complete(GrainId grainId, string idempotencyKey, ReadOnlyMemory<byte> responsePayload);

    /// <summary>Drops all entries for a grain (called from its deactivation callback).</summary>
    void EvictGrain(GrainId grainId);
}

/// <summary>Result of TryBeginAsync.</summary>
public readonly struct DedupLease
{
    public DedupOutcome Outcome { get; }                 // Execute | Replay | Conflict
    public ReadOnlyMemory<byte> RecordedResponse { get; } // valid when Outcome == Replay
}

public enum DedupOutcome { Execute, Replay, Conflict }
```

Default in-memory implementation: `ConcurrentDictionary<GrainId, PerGrainDedupTable>` where each
`PerGrainDedupTable` is a bounded (count-capped, TTL-swept) map keyed by idempotency key; `EvictGrain`
removes the grain's table. Durable implementation wraps the same shape over `IGrainStorage`.

### 8.4 Options + registration (new, `Quark.Core`)

```csharp
public sealed class IdempotencyOptions
{
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);   // TTL; must exceed client retry horizon
    public int MaxEntriesPerGrain { get; set; } = 64;                 // per-grain LRU cap
    public DedupDurability Durability { get; set; } = DedupDurability.InMemory;
    public string? DurableProviderName { get; set; }                  // IGrainStorage provider when Durable
}

public enum DedupDurability { InMemory, Durable }

// ISiloBuilder extension — opt-in, explicit, AOT-safe.
public static ISiloBuilder AddIdempotentCalls(
    this ISiloBuilder builder, Action<IdempotencyOptions>? configure = null);
// Registers IRequestDedupStore, wires the dedup checkpoint into MessageDispatcher.
```

### 8.5 Compatibility tier

| Surface | Tier | Justification |
|---|---|---|
| `QuarkRequestContext.WithIdempotencyKey` + `x-quark-idem` header + `IRequestDedupStore` | **Quark-native** | Neither Orleans nor Akka.NET exposes a caller-supplied idempotency-key primitive. Orleans gets *effectively-once* from internal message resend/duplicate-detection with no public key; Akka offers `AtLeastOnceDelivery` with dedup left to the receiver (`deliveryId`); Azure Durable Functions #1555 is an **open request** for exactly this. No surface to mirror → new concept. |
| `ICallContext.IdempotencyKey` (added member) | **minor-change (additive)** | Extends an existing Quark-native interface; no Orleans equivalent to break. |
| `MessageEnvelope` (`x-quark-idem` header) | **additive** | Reuses the existing string header map; no struct/enum/wire-record change. |
| Dedup does **not** apply to `[Transaction]` | **behavioural, documented** | Preserves Orleans-equivalent transaction semantics unchanged. |

The `QuarkRequestContext` naming intentionally echoes Orleans' `RequestContext` so migrators recognise the
idiom, while staying a distinct, minimal, Quark-native type (we do not reproduce Orleans' full
arbitrary-key request-context bag — only the metadata the engine reads).

---

## 9. AOT / trim safety

- **No reflection, no dynamic codegen.** The key is a string header; the arg hash is a byte-span FNV-1a;
  the recorded outcome is the already-serialized `GrainInvocationResponse` bytes. All reuse the
  AOT-clean codec path.
- **No exception serialization.** Only the `Error` **string** already present on `GrainInvocationResponse`
  is stored and replayed — no exception object crosses the store, so nothing trips `ISerializable`/QRK0003.
- **Explicit registration only** — `AddIdempotentCalls(...)`; no assembly scanning, no discovery. Off
  unless opted in, so the default silo pays nothing and the header path is inert.
- **AsyncLocal** (`QuarkRequestContext`) is fully AOT-compatible; the scope struct/`IDisposable` allocates
  only when a key is actually set.
- **No new package edges.** The store and options live in `Quark.Runtime`/`Quark.Core`; the carrier lives
  in `Quark.Core.Abstractions` (interfaces/value types only — honours the abstractions-package rule).
  `TcpGatewayCallInvoker` already references `Quark.Core.Abstractions.Hosting`, so reading the ambient key
  adds no dependency. Runtime never references client packages.
- **AOT smoke:** extend the existing `PublishAot=true` runtime smoke build with a host calling
  `AddIdempotentCalls` — must stay warning-free; any `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`
  appearing signals design drift.

---

## 10. Testing strategy

- **Unit — key survives retry:** a `TcpGatewayCallInvoker` retried after a simulated timeout re-sends the
  **same** `x-quark-idem` despite a new correlation id; assert the header value is stable across attempts.
- **Unit — dedup replays, does not re-execute:** drive `MessageDispatcher.DispatchRequestAsync` twice with
  the same key against a counting behavior; assert the method body ran **once** and both calls returned
  identical response bytes. (This is #59's exit criterion, delivered here.)
- **Unit — failure outcome replays:** a keyed call whose method throws, retried, replays the **same** error
  and does **not** re-run the side effect (counting probe).
- **Unit — argument conflict rejected:** same key, different argument payload → `DedupOutcome.Conflict` /
  `IdempotencyKeyConflict` failed response; original outcome untouched.
- **Unit — concurrent duplicate:** two in-flight calls with the same key against a reentrant grain → single
  execution; both await the same `InFlight` Task.
- **Unit — eviction / TTL:** entry past `Window` or beyond `MaxEntriesPerGrain` → next retry re-executes
  (documents the window limit); deactivation callback (`EvictGrain`) drops the grain's keys.
- **Unit — transactional bypass:** a request marked `x-quark-tx` (or a `[Transaction]` method) skips the
  dedup checkpoint and executes; a key set on a transactional call is ignored with the documented warning.
- **Integration — cross-silo forwarded call (with #126):** client sets key → gateway A forwards to owner B
  → assert B (terminal) dedups, A does not cache; a retry through A replays B's recorded outcome. Requires
  the `NetworkedTestCluster` harness #126 introduces.
- **Fault (`Quark.Tests.Fault`):** deactivation between original and in-memory-tier retry re-executes
  (proves the documented limitation); the durable tier under the same scenario replays (proves the
  escalation). Server-crash gap asserted at the documented boundary.
- **AOT smoke** per §9.
- **Flaky-test caution (memory):** keep TTL/eviction timing assertions tolerant (generous `Window` in
  tests, deterministic fake clock where possible) — oracle-timing tests already flake under parallel load.

---

## 11. Implementation sequence (circular-dep-safe, top-to-bottom)

1. `Quark.Core.Abstractions/Hosting/QuarkRequestContext.cs` — ambient scoped key carrier (value/utility
   only; abstractions package).
2. `Quark.Core.Abstractions/Hosting/ICallContext.cs` — add `string? IdempotencyKey`; update the scoped
   `ICallContext` implementation + `ICallContextSetter` to accept/stamp the key.
3. `Quark.Transport.Abstractions` — **no change**; confirm `MessageHeaders` carries `x-quark-idem`
   (constant defined in `Quark.Runtime`, e.g. `QuarkHeaders.IdempotencyKey = "x-quark-idem"`).
4. `Quark.Client.Tcp/TcpGatewayCallInvoker.cs` — read `QuarkRequestContext.IdempotencyKey`; when present,
   attach `Headers` with `x-quark-idem` to the outbound `MessageEnvelope`; clear the ambient slot after
   read. (In-process `LocalGrainCallInvoker` needs no change — dedup is transport-only.)
5. `Quark.Runtime/IRequestDedupStore.cs` + `DedupLease`/`DedupOutcome` — the seam.
6. `Quark.Runtime/InMemoryRequestDedupStore.cs` — default bounded per-grain implementation; `EvictGrain`.
7. `Quark.Runtime/GrainActivation` wiring — call `IRequestDedupStore.EvictGrain(grainId)` from the
   existing `SetOnDeactivated` callback so deactivation prunes the grain's keys.
8. `Quark.Runtime/MessageDispatcher.cs` — insert the dedup checkpoint in `DispatchRequestAsync` on the
   local-execution branch: read `x-quark-idem`; skip if absent or if `x-quark-tx` present; else
   `TryBeginAsync` → Execute / Replay / Conflict; `Complete` after execution. Surface the key onto
   `ICallContext` for the scope.
9. `Quark.Runtime/Clustering/SiloCallInvoker.cs` (from #126) — when forwarding, **copy** the inbound
   `x-quark-idem` header onto the forwarded envelope (alongside `x-quark-hop`) so the terminal silo dedups
   on the original key. (Coordinate the header-copy with #126's owner.)
10. `Quark.Runtime/IdempotencyOptions.cs` + `Quark.Core` `AddIdempotentCalls(this ISiloBuilder)` —
    register store + wire the checkpoint; validate `Window > 0` and a `DurableProviderName` when
    `Durable`.
11. (Escalation) `Quark.Runtime/DurableRequestDedupStore.cs` — `IGrainStorage`-backed store for the
    opt-in durable tier; write-before-ack.
12. Tests per §10; wiki: add an *Idempotency & at-most-once* section to `Clustering-and-Transport.md` /
    a failure-semantics page (feeds #128 pillar 4); update `FEATURES.md`.

No step references a forbidden package: the carrier is in `*.Abstractions`; the store/options are silo-side
`Quark.Runtime`/`Quark.Core`; the client only reads an abstractions-level ambient value.

---

## 12. Open questions

1. **Ambient vs. explicit key API (biggest UX call).** `QuarkRequestContext.WithIdempotencyKey` (ambient,
   scoped) is chosen because generated proxies cannot take an extra parameter without breaking drop-in
   interface shapes. The cost is ambient "magic." Alternative: a `client.GetGrain<T>(key,
   idempotencyKey)` factory overload that returns a proxy pinned to one key — explicit, but pins the key
   for the proxy's lifetime (awkward for per-call keys) and adds a proxy variant. **Recommend ambient;**
   confirm the maintainer accepts a `RequestContext`-style idiom in an otherwise
   explicit-over-ambient engine.
2. **In-memory vs. durable as the *default*.** Shipped default is in-memory (cheap, correct for the
   seconds-scale retry window). Some will expect durable-by-default for a feature named "idempotency."
   **Recommend in-memory default + prominent doc of the deactivation limit**, durable opt-in per §5.
   The load-bearing scoping call.
3. **Should the arg-hash conflict *reject* or *ignore*?** Reject (chosen) catches client bugs but turns a
   sloppy key-reuse into a hard error. Alternative: log-and-execute-fresh. **Recommend reject** — silent
   mis-serving of a cached result for different arguments is the worse failure.
4. **`x-quark-tx` marker ownership.** The transactional-bypass guard (§6) depends on a marker the (not yet
   built) transaction middleware must stamp. Until that lands, the guard is inert and transactional methods
   simply must not be given keys. Confirm the future transaction-middleware issue accepts stamping
   `x-quark-tx`.
5. **One dedup store per silo vs. per activation object.** This spec puts the authoritative store behind
   `IRequestDedupStore` (per-silo container, per-grain sub-tables, activation-lifetime eviction) for
   dispatcher decoupling, rather than literally on `GrainActivation`. Observable behaviour is identical
   ("per-activation"); confirm no reviewer prefers the on-activation placement for locality.
6. **Interaction with #126's re-dial-once (§8 of that spec).** A silo→silo forwarded call re-dialed after a
   peer drop re-sends with the same `x-quark-idem`, so the terminal silo dedups it correctly — *provided*
   the terminal silo's activation (and its in-memory dedup entry) survived. If the drop coincided with the
   owner's deactivation, the durable tier is required for exactly-once across that event. Flag for the #60
   retry-replay work.

---

## 13. Dependencies & related work

- **#59** — mechanism merged here (§4); its residual scope is the guarantee-doc table + one-way acks, both
  consuming this store. Recommend relabel + supersede its correlation-id bullet.
- **#126 (silo-to-silo transport)** — shares the `MessageHeaders` mechanism; `SiloCallInvoker` must copy
  `x-quark-idem` onto forwarded envelopes (step 9). Dedup runs only at the terminal silo, identified by the
  local-execution branch (naturally including #126's `x-quark-hop` terminal path).
- **#128 pillar 4 (failure semantics)** — this spec is the concrete "retry, timeout, and idempotency
  story" that pillar requires; its output feeds the failure-semantics documentation surface.
- **#56 (transport auth)** — a forwarded call should dedup on the *original* caller's key; principal
  propagation across the hop is #56's concern, not duplicated here.
- **#60 (failover hygiene)** — retry-safe replay on peer drop should honour idempotency keys so a replayed
  in-flight call does not double-execute; this store is the mechanism it should reuse (§12-Q6).
- **Future (recommended issue):** transactional-outbox / atomic side-effect+dedup-record commit to close
  the crash gap (§2, §7) for genuine exactly-once.
