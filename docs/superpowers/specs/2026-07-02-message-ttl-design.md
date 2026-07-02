# Design: Message TTL / arrival-time expiration
**Issue:** #107
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

Expire requests that have already outlived the caller's patience, so stale work does
not compete with fresh work under backpressure. Expiry is decided **at the receiving
silo from a relative TTL (duration)** — never an absolute deadline — so no clock
synchronisation between caller and silo is required. This mirrors the Orleans
arrival-time fix (dotnet/orleans#1524).

---

## Goals / Non-goals

**Goals**
- A caller stamps a relative TTL (its call budget, in ms) onto outgoing `Request` /
  `OneWayRequest` envelopes.
- The receiving silo, on **arrival**, computes `deadline = arrivalTimestamp + ttl` and
  refuses to execute a request whose deadline has passed, at **two** enforcement points:
  (1) dispatch time (before it reaches a grain mailbox) and (2) mailbox dequeue (it
  expired while queued behind other work in the grain).
- Expired `Request`s are answered with a **typed expired response** (distinguishable from
  a normal failure) so a still-waiting caller can throw `MessageExpiredException`; expired
  `OneWayRequest`s are dropped silently.
- Passive expiry only: no timer armed per request on the cheap primary path. Share the
  per-call *deadline representation* with #37 cancellation where that is cheap.
- Zero wire-format break; AOT-safe (strings + timestamp compares, no reflection).

**Non-goals**
- Absolute/wall-clock deadlines across machines (rejected — requires clock sync).
- Interrupting a behavior that is **already executing** past its deadline (that is active
  cancellation — #37; TTL is passive expiry only). A call that has begun executing runs to
  completion regardless of TTL.
- TTL on server→client pushes and control frames (`StreamPush`, `ObserverInvoke`,
  `System`, `Stream*`/`Observer*` register/unregister) — no waiting caller, no meaningful
  deadline.
- Compensating for network transit time (TTL is re-based to arrival — see Wire
  compatibility; this intentionally over-grants by the transit duration to avoid clock skew).

---

## Proposed API & envelope change

### Wire: no new `MessageEnvelope` field — a header

`MessageEnvelope` (`src/Quark.Transport.Abstractions/MessageEnvelope.cs`) is
`CorrelationId` + `MessageType` + `Payload` + optional `MessageHeaders`. Adding a strongly
typed field would be a wire-format change (see below). Instead the TTL travels as a
**reserved header key** on the existing `MessageHeaders` string map:

| Header | Direction | Meaning |
|---|---|---|
| `q-ttl-ms` | caller → silo, on `Request`/`OneWayRequest` | relative TTL budget in whole milliseconds, `uint` as decimal string |
| `q-expired` | silo → caller, on the expired `Response` | `"1"` marks the response as an expiry, not a behavior failure |

Reserved-key constants live in a new `WellKnownHeaders` static class in
`Quark.Transport.Abstractions` (so both silo and client reference one source of truth).

### Client-side call timeout (this is where the TTL value comes from)

There is currently **no** client call timeout: `TcpGatewayConnection.SendAndAwaitAsync`
waits on the caller-supplied `CancellationToken` only, with no default. A
`CancellationToken` does not expose its deadline, so the TTL value must come from an
explicit option. Introduce a nullable default response timeout on the TCP client:

```csharp
// Quark.Client.Tcp — client gateway options (new field on the existing options type)
public TimeSpan? DefaultResponseTimeout { get; set; } = null; // null = no client timeout, no TTL stamped
```

When set, `TcpGatewayCallInvoker.SendAsync` stamps `q-ttl-ms` from it and
`SendAndAwaitAsync` arms a matching `CancelAfter` on the pending wait so caller and silo
agree on the budget.

### Silo-side default TTL (covers local + inbound calls that carry no header)

```csharp
// SiloRuntimeOptions (Quark.Runtime) — new fields
public TimeSpan? DefaultRequestTtl { get; set; } = null; // applies when an inbound/local request carries no q-ttl-ms
public TimeSpan  MaxRequestTtl     { get; set; } = TimeSpan.FromMinutes(5); // clamp untrusted client values
```

### Typed error surfaced to a still-waiting caller

```csharp
// Quark.Core.Abstractions (new exception, drop-in-adjacent to Orleans' expiry behaviour)
public sealed class MessageExpiredException : Exception { public GrainId GrainId { get; } ... }
```

**Compatibility tiers:** header-based TTL, `q-ttl-ms`/`q-expired`, `WellKnownHeaders`,
`DefaultRequestTtl`, `MaxRequestTtl`, `DefaultResponseTimeout` = **Quark-native** (no
Orleans public surface — Orleans hides this inside its messaging layer).
`MessageExpiredException` = **minor-change** (Orleans throws a timeout-family exception on
the caller; ours is a distinct type). No drop-in grain-authoring surface changes.

---

## Wire compatibility

`MessageSerializer` (`src/Quark.Runtime/MessageSerializer.cs`) encodes an envelope as a
**fixed positional prefix followed by a count-prefixed header map** — there is **no
version byte**:

```
Int64  CorrelationId
Byte   MessageType
VarU32 headerCount
  (String key, String value) * headerCount
Bytes  Payload            // length-delimited, last field
```

Because headers are a self-describing count-prefixed map read by key, **adding the
`q-ttl-ms` / `q-expired` keys is not a wire-format change**: an old peer that does not know
the keys ignores them (its `MessageHeaders.Get("q-ttl-ms")` simply returns `null` → no
TTL), and a new peer treats a missing key as "no TTL". Old↔new interoperate in both
directions with graceful degradation. This is decisive for choosing a header over a new
positional field: a new positional field between `MessageType` and `headerCount`, or after
`Payload`, **would** break the fixed offsets and force a version byte + dual-path decoder.
We explicitly avoid that.

**Arrival-time rebasing.** The silo never trusts a caller clock. On reading a framed
envelope, the receive loop captures `arrivalTs = Stopwatch.GetTimestamp()` and, if
`q-ttl-ms` is present (clamped to `MaxRequestTtl`), computes a monotonic
`deadlineTs = arrivalTs + ttl`. All expiry comparisons use `Stopwatch.GetTimestamp()`
(monotonic, skew-free). This over-grants by the network transit time already elapsed, which
is the accepted trade-off for requiring zero clock sync — and is exactly the property the
Orleans fix delivers.

---

## Enforcement points (anchors)

The TTL/deadline is carried from the receive loop to the mailbox as a monotonic
`long deadlineTs` (`0` = no deadline). Threading approach: **share the #37 per-call
plumbing** — see Dependencies.

**1. Read loop — capture arrival + compute deadline (TCP only).**
- `GatewayMessagePump.ProcessConnectionAsync` (`src/Quark.Runtime/GatewayMessagePump.cs`,
  the `case MessageType.Request/OneWayRequest` arm ~`:194`–`:206`) and
  `SiloMessagePump.ProcessConnectionAsync` (`src/Quark.Runtime/SiloMessagePump.cs:122`–
  `:130`): immediately after `ReadAsync` returns an envelope, read `q-ttl-ms`, clamp to
  `MaxRequestTtl`, compute `deadlineTs`. Pass it into dispatch.

**2. Dispatch-time check — the cheap primary (drop before touching a mailbox).**
- `MessageDispatcher.DispatchRequestAsync` (`src/Quark.Runtime/MessageDispatcher.cs:51`):
  before `DeserializeRequest`/invoke, if `deadlineTs != 0 && GetTimestamp() >= deadlineTs`:
  - `Request` → return an expired `Response` envelope (Headers set `q-expired=1`,
    `GrainInvocationResponse(Success:false, Error:"message expired")`) **without invoking**.
  - `OneWayRequest` → return `null` (dropped silently).
  - Emit `OnMessageExpired` diagnostic; bump a metric. This is a pure timestamp compare —
    **no timer armed** — and catches the dominant case (message sat in a connection/OS/
    backpressure queue before dispatch).
- `MessageDispatcher.DispatchAsync` needs the `deadlineTs`; add an optional
  `long deadlineTs = 0` parameter to `IMessageDispatcher.DispatchAsync` (both pumps are the
  only callers; default keeps other callers/tests source-compatible).

**3. Mailbox-dequeue check — expired while queued in a busy grain.**
- A request that passed the dispatch check can still sit behind other work in
  `GrainActivation._queue`. `GrainActivation.RunLoopAsync`
  (`src/Quark.Runtime/GrainActivation.cs:509`) already loops per work item; add a
  per-work-item `long DeadlineTs` on `MailboxWorkItem` (default `0`). At dequeue, before
  `ExecuteAsync`, if `DeadlineTs != 0 && GetTimestamp() >= DeadlineTs`, **skip execution**
  and complete the awaiter with `MessageExpiredException` (for awaited items) so the
  behavior is **never constructed** — mirroring the #37 queued-cancel decision. Fire-and-
  forget items (timers/deactivation) always pass `0`.
- The deadline reaches the work item without churning `IGrainCallInvoker`: reuse the #37
  effective-cancellation-token channel. When #37 lands, `MessageDispatcher` builds the
  per-call token; for TTL it links a CTS with `CancelAfter(remaining)` **only when the call
  reaches the mailbox path**, so the #37 queued-work `ThrowIfCancellationRequested()` at
  dequeue fires for TTL-expired calls for free. **If #37 has not landed**, add an optional
  `PostAsync(Func<ValueTask> workItem, long deadlineTs = 0)` overload and thread `deadlineTs`
  from `LocalGrainCallInvoker`'s invoke (captured in the existing work-item closure at
  `:93`/`:148`) — a single timestamp compare, no timer. Recommended: land the dispatch-time
  check first (fully standalone), then wire the dequeue check onto whichever of the two
  channels exists.

**Local (non-TCP) calls.** Recommendation: **the dequeue check is universal** (one
`DeadlineTs != 0` compare, negligible on the hot path), but local calls carry **no TTL by
default** — there is no envelope and a local caller's `CancellationToken` deadline is not
readable. A local call gets a deadline only when `SiloRuntimeOptions.DefaultRequestTtl` is
configured, in which case `LocalGrainCallInvoker` stamps `deadlineTs = now + DefaultRequestTtl`
into the work item. This keeps the zero-config local hot path allocation- and timer-free
while making TTL available and consistent when opted in.

**One-way / stream messages.** `OneWayRequest`: TTL **applies**, silently dropped on
expiry (no response path). `StreamPush`, `ObserverInvoke`, `System`, and all
subscribe/register control frames: **no TTL** (Non-goals).

**Orphaned-correlation safety (verified).** When the silo returns an expired `Response`,
the client has typically already given up: `TcpGatewayConnection.SendAndAwaitAsync`
(`src/Quark.Client.Tcp/TcpGatewayConnection.cs:88`) removes its `_pending[id]` entry via the
`ct.Register` callback on caller cancel/timeout, and `FaultAllPending` clears everything on
disconnect. So when the expired `Response` arrives, `ReadLoopAsync`'s
`_pending.TryRemove(id)` (`:169`) returns `false` and the frame is **discarded harmlessly —
no hang, no leak**. If the client is still waiting (its timeout exceeded the silo TTL, e.g.
misconfigured), `TcpGatewayCallInvoker` inspects `response.Headers.Get("q-expired")` and
throws `MessageExpiredException` instead of the generic `InvalidOperationException`.

---

## Diagnostics

Follows the existing `readonly struct` event + defaulted `IQuarkDiagnosticListener` method
convention (see `MessageDispatchedEvent`). Add:

```csharp
// Quark.Diagnostics.Abstractions/Events/MessageExpiredEvent.cs
public readonly struct MessageExpiredEvent(
    GrainId grainId, MessageType messageType, TimeSpan queuedFor, MessageExpiryStage stage)
{
    public GrainId GrainId { get; } = grainId;
    public MessageType MessageType { get; } = messageType;
    public TimeSpan QueuedFor { get; } = queuedFor;          // now - arrival
    public MessageExpiryStage Stage { get; } = stage;         // Dispatch | Mailbox
}
public enum MessageExpiryStage { Dispatch, Mailbox }
```

- `IQuarkDiagnosticListener`: add `void OnMessageExpired(in MessageExpiredEvent e) { }`
  (defaulted no-op, so no listener breaks). `NullDiagnosticListener` inherits the default.
- `CompositeDiagnosticListener`: fan-out override (mechanical, matches siblings).
- `QuarkInstruments`: add a `MessagesExpired` counter tagged
  `grain_type` + `stage` (mirrors `GatewayMessagesReceived` shape). At the dispatch anchor
  the `GrainId` is available from the deserialized request; at the mailbox anchor from the
  activation.

---

## AOT notes

- No new `MessageEnvelope` field, no version byte, no polymorphic serialization — TTL is a
  decimal-string header value and monotonic `long` timestamps. No reflection, no
  `ISerializable` (QRK0003 clean), no dynamic code.
- All expiry checks are `Stopwatch.GetTimestamp()` compares — trim/AOT-safe and allocation-
  free. The no-TTL path (missing header, unconfigured default) adds at most one dictionary
  `Get` + one `long != 0` branch; nothing on the fully-local zero-config hot path when
  `DefaultRequestTtl` is null.
- `MessageExpiredException`, `MessageExpiredEvent`, `MessageExpiryStage`, `WellKnownHeaders`
  are plain types in existing trimmable packages. Client TTL timer (`CancelAfter`) is armed
  only when `DefaultResponseTimeout` is set.

---

## Test plan

Unit/integration, hand-wiring invoker/proxy per house style:

1. **Header round-trips** — `MessageSerializer` serialize→deserialize preserves `q-ttl-ms`;
   an envelope built by an "old" encoder (no key) deserializes with no TTL (compat).
2. **Dispatch-time expiry (Request)** — arrival with `ttl=0`/already-past deadline → behavior
   **never constructed** (construction counter), caller gets an expired `Response` carrying
   `q-expired=1`; `MessageExpiredException` surfaced when still waiting.
3. **Dispatch-time expiry (OneWayRequest)** — expired one-way is dropped, `null` returned, no
   response frame written.
4. **Mailbox-dequeue expiry** — occupy a grain's mailbox with a slow call, enqueue a second
   request with a short TTL, ensure it expires at dequeue and its behavior is never
   constructed. (Shares the #37 queued-cancel harness if present.)
5. **Not-expired passes** — generous TTL executes normally; result returned.
6. **Mid-execution is NOT interrupted** — a behavior that starts before the deadline and runs
   past it completes (passive-expiry guarantee; Non-goal assertion).
7. **Orphaned correlation** — client cancels/times out, then the silo's expired `Response`
   arrives; assert `_pending` is empty and the frame is silently dropped (no exception, no
   leak).
8. **Local default TTL** — `SiloRuntimeOptions.DefaultRequestTtl` set; a local call queued
   behind slow work expires at dequeue; unset → no expiry, zero overhead.
9. **Clamp** — a client TTL above `MaxRequestTtl` is clamped.
10. **Diagnostics** — `OnMessageExpired` fires with correct `Stage`/`QueuedFor`;
    `MessagesExpired` counter increments per stage.
11. **AOT smoke** — a sample publishes with `PublishAot=true` exercising an expiring call.

---

## Implementation checklist

- [ ] `WellKnownHeaders` (`q-ttl-ms`, `q-expired`) in `Quark.Transport.Abstractions`.
- [ ] `MessageExpiredException` in `Quark.Core.Abstractions`.
- [ ] `SiloRuntimeOptions.DefaultRequestTtl` + `MaxRequestTtl`.
- [ ] `DefaultResponseTimeout` on the TCP client gateway options.
- [ ] `IMessageDispatcher.DispatchAsync` gains optional `long deadlineTs = 0`;
      `MessageDispatcher.DispatchRequestAsync` dispatch-time expiry + expired `Response`.
- [ ] `GatewayMessagePump` + `SiloMessagePump`: capture arrival ts, parse+clamp `q-ttl-ms`,
      compute `deadlineTs`, pass into dispatch.
- [ ] `TcpGatewayCallInvoker.SendAsync`: stamp `q-ttl-ms`; `SendAndAwaitAsync`:
      `CancelAfter` when `DefaultResponseTimeout` set; map `q-expired` response →
      `MessageExpiredException`.
- [ ] Mailbox-dequeue check: `MailboxWorkItem.DeadlineTs` + `RunLoopAsync` compare +
      complete-with-`MessageExpiredException`; wired via #37 token **or** a
      `PostAsync(..., long deadlineTs)` overload threaded from `LocalGrainCallInvoker`.
- [ ] `MessageExpiredEvent` + `MessageExpiryStage`; `IQuarkDiagnosticListener.OnMessageExpired`
      default; `CompositeDiagnosticListener` fan-out; `QuarkInstruments.MessagesExpired`.
- [ ] Tests 1–11; wiki `Clustering-and-Transport` (TTL section); `FEATURES.md` parity entry.

---

## Resolved design decisions

**D1 — Header, not a new envelope field.** The wire format has no version byte; headers are
a self-describing count-prefixed map. A header is zero-break and old↔new interoperable; a
positional field is not. Chosen: header.

**D2 — Relative TTL rebased to arrival, not an absolute deadline.** Absolute deadlines need
clock sync; relative TTL + arrival rebasing needs none, at the cost of over-granting by
transit time. This is the Orleans arrival-time semantics and the issue's stated rationale.

**D3 — Two enforcement points, both timestamp compares.** Dispatch-time is the cheap primary
(catches connection/backpressure queueing, no timer). Mailbox-dequeue catches expiry behind
a busy grain and reuses the #37 queued-work check. No per-request timer on the primary path.

**D4 — Passive expiry only; executing calls run to completion.** Interrupting a running
behavior is active cancellation (#37). TTL only prevents *starting* stale work. Keeps the two
features cleanly separated while sharing the deadline representation.

**D5 — Local calls: unified enforcement point, opt-in TTL.** The dequeue compare is always
present (negligible), but local calls carry a deadline only when `DefaultRequestTtl` is set,
preserving the zero-config local hot path.

**D6 — One-way expires (silent drop); pushes/control frames do not.** No waiting caller for
pushes/control frames; one-way has a real "already-given-up" case.

**D7 — Typed expired response via `q-expired` header, not error-string parsing.** A still-
waiting caller throws `MessageExpiredException`; a caller that already gave up drops the frame
harmlessly (verified against `_pending` cleanup). Distinguishing via a header avoids brittle
`Error` string matching.

**D8 — Client TTL source = a new `DefaultResponseTimeout` option.** No client call timeout
exists today and `CancellationToken` deadlines are unreadable, so the stamped TTL must come
from an explicit option; this also gives Quark its first client-side call timeout.

---

## Dependencies & related work

- **#37 grain cancellation** (`docs/superpowers/specs/2026-07-02-grain-cancellation-token-design.md`):
  strongly related. #37 establishes a per-call effective-cancellation-token channel and a
  **queued-work `ThrowIfCancellationRequested()` at dequeue** — exactly the hook TTL's
  mailbox-dequeue check needs. **Recommendation:** land #37's dequeue check first (or
  concurrently) and drive TTL's mailbox expiry through it via a `CancelAfter`-linked CTS
  built in `MessageDispatcher`; if TTL ships first, add the standalone `PostAsync` deadline
  overload and migrate to the shared channel when #37 lands. TTL's **dispatch-time** check is
  fully independent of #37 and can ship immediately. Cancellation = active, TTL = passive;
  they share the deadline representation, not the trigger.
- Interacts with `SiloRuntimeOptions.CollectionAge`/`GrainIdleCollector` only cosmetically
  (both use monotonic time; no shared state).
- `[Transaction]`/2PC: an expired-at-dequeue transactional request is dropped before
  enlistment (safe); TTL never aborts an in-flight 2PC (consistent with #37 Q7).

## Open questions

1. `q-ttl-ms` granularity is whole ms as `uint` (max ~49 days) — sufficient? (Recommend yes.)
2. Should `DefaultRequestTtl`, when set, also stamp `q-ttl-ms` on **silo→silo** forwarded
   requests (`LocalGrainCallInvoker.TryRouteRemote` path), or is arrival-rebasing at each hop
   enough? (Recommend arrival-rebasing per hop; do not propagate a shrinking budget in v1.)
3. Do we want a client-config knob to make a still-waiting caller **retry** on
   `MessageExpiredException`, or always surface it? (Recommend surface-only in v1.)
</content>
</invoke>
