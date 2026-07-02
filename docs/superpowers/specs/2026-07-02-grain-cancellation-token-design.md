**Issue:** #37 — GrainCancellationToken; consolidates #113  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

# Design: `GrainCancellationToken` — cancel in-flight grain calls (#37, consolidates #113)

## Goals / Non-goals

**Goals**
- A caller can cancel a grain call and have the cancellation reach the executing behavior, both **in-process** and **across the TCP gateway** (fixes #113).
- Cancel a call that is still **queued** in a grain's mailbox (never executes the behavior).
- Cancel a call that is **mid-execution** (behavior observes the token cooperatively).
- Orleans **drop-in** surface: `GrainCancellationToken` / `GrainCancellationTokenSource`, plus a Quark-native ambient accessor `ICallContext.CallCancellation`.
- Wire format is a **`Guid` only**; no reflection, no polymorphic serialization; explicit DI registration.

**Non-goals**
- Forcibly aborting a running behavior thread (cancellation is cooperative — the behavior must observe the token).
- Multi-gateway / silo-to-silo cancel fan-out for a token sent to *several* silos in v1 (single active gateway connection per client is the supported topology; the runtime is structured to extend later).
- Forced abort of a `[Transaction]`/2PC call (see Resolved decisions — cooperative-only).
- Cancelling stream pushes or observer callbacks.

---

## Proposed API

### `Quark.Core.Abstractions` (new)

```csharp
namespace Quark.Core.Abstractions.Hosting;

// Drop-in tier: mirrors Orleans.GrainCancellationToken
public sealed class GrainCancellationToken
{
    public Guid Id { get; }
    public CancellationToken CancellationToken { get; }

    // Wraps an existing token/id (used by the client source and the silo runtime).
    public GrainCancellationToken(Guid id, CancellationToken cancellationToken);

    // Convenience for callers who already hold a CancellationToken.
    public static GrainCancellationToken FromCancellationToken(CancellationToken ct);
}

// Drop-in tier: mirrors Orleans.GrainCancellationTokenSource
public sealed class GrainCancellationTokenSource : IDisposable
{
    public GrainCancellationTokenSource();                 // pure-local usage (Orleans-style new)
    public GrainCancellationToken Token { get; }           // stable Id + CTS-backed token
    public Task Cancel();                                   // cancels local CTS; fires remote-propagation callbacks
    public void Dispose();
}

// Silo-side reconstruction/cancel registry. Abstraction lives here so the codegen'd
// dispatcher (which only references Core.Abstractions) can take it as a parameter.
public interface IGrainCancellationTokenRuntime
{
    // Get-or-create the silo-local CTS for this Id and return a token bound to it.
    // If the Id was already cancelled (cancel-before-request race) the returned token is
    // already in the cancelled state.
    GrainCancellationToken GetOrCreate(Guid id);
    void Cancel(Guid id);      // idempotent; tolerant of unknown Id (records a bounded pre-cancel marker)
    void Release(Guid id);     // dispose + remove the CTS; clears any pre-cancel marker
}
```

### `ICallContext` extension (Quark-native tier)

```csharp
public interface ICallContext
{
    GrainId GrainId { get; }
    CancellationToken CallCancellation { get; }   // NEW — the per-call cancellation token
}

// engine-internal
public interface ICallContextSetter
{
    void Set(GrainId grainId, CancellationToken callCancellation);   // signature widened
}
```

### Invokable extension (Quark-native, zero-alloc)

```csharp
public interface IGrainInvokable<TResult>
{
    // ... existing members ...
    GrainCancellationToken? CallCancellation => null;   // default no-op (hand-written test invokables)
}
public interface IGrainVoidInvokable
{
    // ... existing members ...
    GrainCancellationToken? CallCancellation => null;
}
```

The generator **always** emits a concrete `public GrainCancellationToken? CallCancellation => ...;` on every invokable struct (returns the stored token when the method has a GCT parameter, otherwise `null`). Because the runtime reads it through a `where TInvokable : struct` constraint, the call binds to the struct's own member — **no boxing, no default-interface dispatch** on the hot path. The default interface member exists only so hand-written test invokables compile unchanged.

### Grain method authoring (drop-in)

```csharp
Task<Result> LongRunningAsync(string input, GrainCancellationToken gct);
```

or Quark-native, no parameter:

```csharp
public async Task<Result> LongRunningAsync(string input)
{
    _ctx.CallCancellation.ThrowIfCancellationRequested();
    // ...
}
```

**Compatibility tiers:** `GrainCancellationToken`/`GrainCancellationTokenSource` = **drop-in**. `ICallContext.CallCancellation`, `MessageType.CancelRequest`, invokable `CallCancellation`, `ITransportGrainDispatcher` signature = **Quark-native / internal**.

---

## Runtime integration (real anchors)

### 1. Per-call token flows through the existing `cancellationToken` channel

The unifying idea: the effective per-call cancellation token is derived from the invokable itself, so `IGrainCallInvoker`'s signature does **not** change (it recently moved to `ValueTask` returns — we avoid re-churning it).

- **`GrainProxyGenerator.EmitMethod`** (`src/Quark.CodeGenerator/GrainProxyGenerator.cs:951`): when a method has a `GrainCancellationToken` parameter, thread `<gctParam>.CancellationToken` as the `cancellationToken` argument to `_invoker.InvokeAsync/InvokeVoidAsync(...)`. The GCT is *also* still a serialized method argument.
- **`LocalGrainCallInvoker.InvokeAsync` / `InvokeVoidAsync`** (`src/Quark.Runtime/LocalGrainCallInvoker.cs:67,123`): compute
  `CancellationToken callCt = invokable.CallCancellation?.CancellationToken ?? cancellationToken;`
  and use `callCt` for scope binding + the queued-work check. This works uniformly for the local path (client's real token flows in via `Clone()` sharing the reference) and the silo receive path (dispatcher fills the invokable with a reconstructed token — see §4).

### 2. Surface the token into the scope + queued-work abort

- **`GrainScopeBinder.BindAndResolveAsync`** (`src/Quark.Runtime/GrainScopeBinder.cs:16`): call `callContextSetter.Set(activation.GrainId, callCt)` (widened). Thread `callCt` in from the invoker (already passed as `cancellationToken`).
- **`CallContext`** (`src/Quark.Runtime/CallContext.cs`): store and expose `CallCancellation`.
- **Queued-call drop:** inside the `activation.PostAsync(async () => { ... })` work item in both `LocalGrainCallInvoker` overloads (`:93` and `:148`), add `callCt.ThrowIfCancellationRequested();` as the first statement — **before** `GrainScopeBinder.BindAndResolveAsync`. A call cancelled while queued throws `OperationCanceledException` without ever constructing the behavior; the mailbox slot is freed via the existing `MailboxWorkItem` completion path. (Filter OCE out of the `GrainInvocationErrors` metric in the existing `catch`.)

### 3. Client remote-cancel wiring (TCP)

- New **`GrainCancellationTokenClientRuntime`** in `Quark.Client.Tcp` (holds the `TcpGatewayConnection`). When `TcpGatewayCallInvoker.InvokeAsync/InvokeVoidAsync` (`src/Quark.Client.Tcp/TcpGatewayCallInvoker.cs:30,54`) sees `invokable.CallCancellation is { } gct`, it calls `clientRuntime.RegisterForCancellation(gct, _connection)`, which **idempotently** does `gct.CancellationToken.Register(() => connection.SendOneWayAsync(cancelEnvelope))` (per `(Id, connection)`). This is Orleans-style lazy registration: `GrainCancellationTokenSource.Cancel()` fires the CTS → the registered callback sends the control frame. `SendOneWayAsync` already exists (`TcpGatewayConnection.cs:99`).
- In a **pure in-process host** there is no client runtime; `Cancel()` simply cancels the CTS and the behavior observes the same token directly. No wire traffic.

### 4. Silo receive path — reconstruction + cleanup

- **`ITransportGrainDispatcher.DispatchAsync`** (`src/Quark.Core.Abstractions/Hosting/ITransportGrainDispatcher.cs`) gains a trailing `IGrainCancellationTokenRuntime? gcRuntime = null` parameter (nullable — hand-written test dispatchers pass nothing; the concrete impls are regenerated).
- **`MessageDispatcher`** (`src/Quark.Runtime/MessageDispatcher.cs`) injects `IGrainCancellationTokenRuntime?` and passes it into `dispatcher.DispatchAsync(...)` at `:78`.
- **Generated `{Proxy}_TransportDispatcher.DispatchAsync`** (`GrainProxyGenerator.EmitTransportDispatcher` `:1018`): for a method with a GCT parameter —
  ```csharp
  var invokable = Struct.Deserialize(ref _reader, factory, gcRuntime);   // reconstructs the GCT arg
  Guid __gcId = invokable.CallCancellation is { } __t ? __t.Id : default;
  try   { await invoker.InvokeVoidAsync(grainId, invokable, ct); }        // LocalGrainCallInvoker reads invokable.CallCancellation
  finally { if (__gcId != default) gcRuntime?.Release(__gcId); }
  ```
- **Generated `Struct.Deserialize`** (`GrainProxyGenerator.EmitSerializeMethods` `:796`): gains `IGrainCancellationTokenRuntime? gcRuntime = null`. For the GCT parameter it reads the 16-byte `Guid` and reconstructs `gcRuntime?.GetOrCreate(id) ?? GrainCancellationToken.FromCancellationToken(CancellationToken.None)`. All other parameters unchanged.

### 5. Silo control-message handling (must not be serialized behind an in-flight call)

- New **`GrainCancellationTokenRuntime`** (silo singleton, `Quark.Runtime`) — `ConcurrentDictionary<Guid, CancellationTokenSource>` + a bounded, time-evicted pre-cancel set. Registered by `AddQuarkRuntime()`.
- **`MessageType.CancelRequest`** is handled **inline on the read loop**, not through the awaited grain-dispatch path:
  - **`GatewayMessagePump`** (`src/Quark.Runtime/GatewayMessagePump.cs:173` switch): add `case MessageType.CancelRequest:` that reads the 16-byte Guid from `envelope.Payload` and calls `_gcRuntime?.Cancel(id)` **synchronously** (no `await` of grain work). One-way, no response.
  - **`SiloMessagePump.ProcessConnectionAsync`** (`src/Quark.Runtime/SiloMessagePump.cs:120`): same inline handling for the silo-to-silo path (peek `MessageType` before the `DispatchAsync` await).

### 6. Concurrent request dispatch (the load-bearing change #113 requires)

**Both pumps currently `await _dispatcher.DispatchAsync(envelope, …)` serially inside the per-connection read loop** (`GatewayMessagePump.cs:206`, `SiloMessagePump.cs:129`). A long-running or mailbox-queued `Request` therefore blocks the read loop from ever reading the following `CancelRequest` frame on the same connection — so same-connection in-flight cancel is impossible without decoupling dispatch from the read loop.

Change: dispatch `Request` / `OneWayRequest` frames as **tracked background tasks** bounded by a per-connection semaphore (new `SiloRuntimeOptions.MaxConcurrentCallsPerConnection`, default e.g. 64; `0` = serial/legacy). Responses are written under the **existing `writeLock`** (`GatewayMessagePump.cs:154/225`), which already serialises the socket. In-flight tasks are tracked in a list and drained in the connection's `finally` before close. `CancelRequest` continues to run inline on the read loop, so it takes effect immediately while grain calls are outstanding.

This also removes a latent throughput limitation (a single client connection currently serialises all of that client's concurrent calls at the gateway). Flagged as the highest-risk change — see Resolved decisions for a phased option.

---

## Wire protocol changes

- **`MessageType`** (`src/Quark.Transport.Abstractions/MessageType.cs`): add `CancelRequest = 10` (one-way, client → silo). No response frame.
- **`CancelRequest` payload:** exactly 16 bytes — the `GrainCancellationToken.Id` (`Guid`), written with `writer.WriteRaw(id.ToByteArray())` / read with `new Guid(reader.ReadRaw(16))`, matching the existing `SerializeKind.Guid` encoding. `CorrelationId` unused (`-1`, like `StreamPush`).
- **GCT method argument:** a new `SerializeKind.GrainCancellationToken` in `GrainProxyGenerator`. `DetermineSerializeKind` matches type `Quark.Core.Abstractions.Hosting.GrainCancellationToken`. Write = 16-byte `Id` Guid. Read = `gcRuntime?.GetOrCreate(id)`. `CloneKind.None` (reference-shared on the local path so the same token object reaches the behavior). No change to `MessageEnvelope`/`MessageHeaders`.

---

## AOT & performance notes

- Wire contract is a fixed 16-byte `Guid`; no polymorphic/`ISerializable` path; no reflection. Consistent with existing `SerializeKind.Guid`.
- `Guid → CTS` registries (silo and pre-cancel set) are plain `ConcurrentDictionary`; no reflection or dynamic code.
- `invokable.CallCancellation` is read through a `struct` generic constraint → devirtualised, **no boxing**; invokables without a GCT parameter return `null` from a concrete struct member (also no box).
- Hot path for the common no-cancellation call is unchanged: `CallCancellation` is `null`, `callCt` falls back to the existing `cancellationToken`, and the queued-work check is a single `ThrowIfCancellationRequested()` on `CancellationToken.None` (no allocation).
- Concurrent dispatch adds one bounded semaphore + a task-tracking list per connection; response ordering is not guaranteed after this change, but grain calls were already multiplexed by `CorrelationId` on the client (`_pending` map, `TcpGatewayConnection.cs:22`), so no client-visible ordering contract is broken.
- Registry cleanup is O(1) `Release` in the dispatcher `finally`; pre-cancel markers are bounded and time-evicted to avoid unbounded growth from spurious cancels.

---

## Test plan

Unit / integration (`Quark.Tests.Unit`, `Quark.Tests.Integration`), hand-writing invoker/proxy/dispatcher per house style:

1. **Local mid-execution cancel** — behavior awaits `gct.CancellationToken`; `GrainCancellationTokenSource.Cancel()` → method throws `OperationCanceledException`.
2. **Local queued cancel** — occupy the grain's mailbox with a slow call, enqueue a second call, cancel it while queued → second behavior is **never constructed**; caller gets OCE. (Assert via a behavior-construction counter.)
3. **`ICallContext.CallCancellation`** — behavior with no GCT parameter reads the ambient token and observes cancellation.
4. **TCP mid-execution cancel** — over `TestCluster` TCP gateway: long-running remote call, `Cancel()` sends `CancelRequest`, silo CTS fires, behavior throws. Assert the silo actually stopped (side-effect counter frozen).
5. **TCP concurrent dispatch** — two overlapping calls on one connection; cancel one, the other completes. Guards the §6 read-loop-not-blocked property.
6. **Completion-before-cancel race** — call completes, then `Cancel()` arrives → no exception, `Cancel(unknownId)` is a no-op; assert no CTS leak (`Release` ran).
7. **Cancel-before-request race** — deliver `CancelRequest(id)` before the `Request`; `GetOrCreate(id)` returns an already-cancelled token → call drops immediately.
8. **Registry cleanup** — after N completed cancellable calls, silo registry dictionary count returns to baseline.
9. **Transaction policy** — `[Transaction]` method with a GCT parameter: cancel does **not** force-abort the 2PC; behavior may observe the token cooperatively (see Resolved decisions).
10. **Codegen** (`Quark.Tests.CodeGenerator`) — snapshot the emitted proxy/invokable/dispatcher for a method with a GCT parameter: `Id`-only ser/deser, `CallCancellation` property, `gcRuntime` threading, `Release` in `finally`.
11. **AOT smoke** — the Persistence or a dedicated sample publishes with `PublishAot=true` exercising a cancellable grain.

---

## Implementation checklist

- [ ] `GrainCancellationToken` (Id + CancellationToken, `FromCancellationToken`) in `Quark.Core.Abstractions.Hosting`.
- [ ] `GrainCancellationTokenSource` (CTS + Id, `Token`, `Cancel()`, `Dispose`) in `Quark.Core.Abstractions.Hosting`.
- [ ] `IGrainCancellationTokenRuntime` abstraction in `Quark.Core.Abstractions.Hosting`.
- [ ] Extend `ICallContext` with `CallCancellation`; widen `ICallContextSetter.Set`; update `CallContext`.
- [ ] Add default `CallCancellation` member to `IGrainInvokable<T>` and `IGrainVoidInvokable`.
- [ ] Add `IGrainCancellationTokenRuntime?` param to `ITransportGrainDispatcher.DispatchAsync`.
- [ ] `MessageType.CancelRequest = 10`.
- [ ] `GrainCancellationTokenRuntime` silo singleton (`Guid→CTS` + bounded pre-cancel set); register in `AddQuarkRuntime()`.
- [ ] `LocalGrainCallInvoker`: derive `callCt` from `invokable.CallCancellation`; queued-work `ThrowIfCancellationRequested`; OCE-filter the error metric; thread `callCt` into `BindAndResolveAsync`.
- [ ] `GrainScopeBinder` + `CallContext`: set/expose `CallCancellation`.
- [ ] `MessageDispatcher`: inject + pass `gcRuntime`.
- [ ] `GatewayMessagePump` + `SiloMessagePump`: inline `CancelRequest` handling; concurrent bounded `Request` dispatch with in-flight task draining.
- [ ] `SiloRuntimeOptions.MaxConcurrentCallsPerConnection`.
- [ ] `TcpGatewayConnection`: build/send `CancelRequest` one-way frame (reuse `SendOneWayAsync`).
- [ ] `GrainCancellationTokenClientRuntime` in `Quark.Client.Tcp`; `TcpGatewayCallInvoker` registers GCT for remote cancel; DI wiring in the client builder.
- [ ] `GrainProxyGenerator`: `SerializeKind.GrainCancellationToken` (Id-only, `CloneKind.None`); emit `CallCancellation` struct member; thread `gct.CancellationToken` into proxy invoke; `Deserialize` + dispatcher take `gcRuntime`, reconstruct arg, `Release` in `finally`.
- [ ] `BehaviorRegistrationGenerator`: no signature break (registers the silo `IGrainCancellationTokenRuntime` if not covered by `AddQuarkRuntime`).
- [ ] Tests 1–11 above; wiki updates (`Clustering-and-Transport`, `Writing-Grains`); `FEATURES.md` parity entry.

---

## Resolved design decisions

**Q1 (#37 draft): where does the reconstructed token bind, and how does the dispatcher reach the registry?**
The draft says the dispatcher "creates/looks up the CTS." Verified against source: the generated `{Proxy}_TransportDispatcher` is a stateless `static Instance` with a fixed `ITransportGrainDispatcher` signature and no DI, and it calls `Struct.Deserialize(ref reader, factory)` which reads args **sequentially** — the Guid can't be pulled out-of-band. **Decision:** thread an `IGrainCancellationTokenRuntime?` parameter through `ITransportGrainDispatcher.DispatchAsync` **and** the generated `Deserialize` (mirroring how `IGrainFactory? factory` is already threaded for grain-ref reconstruction). Reconstruct the GCT arg inside `Deserialize`; `Release` in the dispatcher `finally`. Rationale: consistent with the existing `factory` pattern, no ambient statics, keeps `IGrainCallInvoker` untouched.

**Q2 (#37 draft): "thread the token through `activation.PostAsync`."**
Verified `PostAsync(Func<ValueTask>)` takes only a work item and is shared by timers/deactivation; widening it is invasive and wrong-layered. **Decision:** do **not** change `PostAsync`. Derive `callCt` in `LocalGrainCallInvoker`, capture it in the work-item closure, and do the `ThrowIfCancellationRequested()` there. This is where queued-vs-executing is actually observable.

**Q3 (#37 draft): "extend `ICallContext` vs `IGrainContext`."**
Confirmed `IGrainContext` is stale/unwired (#78) and `ICallContext` currently exposes only `GrainId`. **Decision:** extend `ICallContext` with `CallCancellation`; ignore `IGrainContext`.

**Q4 (#37 draft): `IGrainCallInvoker` cancellation flow after the ValueTask switch.**
The interface already carries `CancellationToken cancellationToken`. **Decision:** reuse it as the per-call token channel and derive its value from `invokable.CallCancellation` on the silo receive path — **no signature change** to the freshly-changed `IGrainCallInvoker`.

**Q5 (#113): out-of-band cancel message vs. deadline-in-request.**
**Decision:** out-of-band **`MessageType.CancelRequest`** carrying the `Guid`. A deadline-in-envelope only expresses timeouts, not explicit `Cancel()`, which is the requested capability. This forces the §6 concurrency change; a deadline header can be added later as an orthogonal optimisation.

**Q6 (NEW — biggest delta from the draft): serial dispatch blocks same-connection cancel.**
Not mentioned in #37/#113. Verified both pumps `await` `DispatchAsync` inline on the read loop, so an in-flight call blocks reading the `CancelRequest`. **Decision:** dispatch `Request`/`OneWayRequest` as bounded background tasks and handle `CancelRequest` inline on the read loop. **Phasing option to de-risk:** Phase 1 ships local + queued + `ICallContext.CallCancellation` cancellation (no pump changes); Phase 2 ships TCP in-flight cancel with the concurrency change. This lets the higher-risk pump rewrite land and bake separately.

**Q7 (#113 open question): interaction with `[Transaction]` / 2PC.**
**Decision — cooperative-only, never a forced abort.** (a) A transactional call still *queued* in the mailbox (nothing enlisted yet) is safe to drop on cancel — the queued-work check applies. (b) Once the behavior is executing under a `[Transaction]`, cancellation is surfaced via the token but the runtime does **not** cancel the 2PC coordinator or roll back mid-protocol; atomicity is governed solely by the transaction's own commit/abort/timeout path. The behavior may cooperatively check the token between transactional operations. Rationale: force-cancelling mid-2PC risks partial commit and violates the atomicity guarantee `[Transaction]` exists to provide. Follow-up: an analyzer (QRK00xx) could warn when a method combines `[Transaction]` with a `GrainCancellationToken` parameter to set the cooperative-only expectation explicitly.

**Q8 (NEW): completion-vs-cancel and cancel-before-request races.**
**Decision:** `IGrainCancellationTokenRuntime.Cancel(unknownId)` is a no-op that records a **bounded, time-evicted pre-cancel marker**; a later `GetOrCreate(id)` for a pre-cancelled Id returns an already-cancelled token (handles cancel arriving before the request registers its CTS). `Release` clears the marker and disposes the CTS. All operations are `ConcurrentDictionary` TryGet/TryRemove — tolerant of interleaving, no exceptions on the race.
