# F-15: TCP Gateway Client — Design Spec

**Date:** 2026-06-06
**Feature:** F-15 — TCP Gateway Client (`UseLocalhostGateway`, `TcpGatewayClusterClient`)
**Complexity:** XL
**Prerequisite for:** Orleans Adventure sample port

---

## Problem

Quark only has `LocalClusterClient` — an in-process client that requires the silo to run in the same
process. The Orleans Adventure sample and any real two-process scenario require a client that connects
to a silo over TCP and invokes grains remotely. This feature closes that gap.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Client process                                                   │
│                                                                  │
│  IClusterClient (TcpGatewayClusterClient)                       │
│    └─ IGrainFactory (TcpGatewayGrainFactory)                    │
│         └─ IGrainCallInvoker (TcpGatewayCallInvoker)            │
│              └─ TcpGatewayConnection ─────────────────── TCP ──►│
└─────────────────────────────────────────────────────────────────┘
                                                          │
┌─────────────────────────────────────────────────────────┼────────┐
│ Silo process                                            │        │
│                                                         │        │
│  GatewayMessagePump (listens :30000) ◄──────────────────┘        │
│    └─ MessageDispatcher(IGrainFactory)                           │
│         └─ ITransportGrainDispatcher (per grain type)           │
│              └─ IGrainCallInvoker (LocalGrainCallInvoker)       │
│                   └─ GrainActivation → Grain method             │
└──────────────────────────────────────────────────────────────────┘
```

**Wire protocol:** unchanged. The existing length-prefixed `MessageEnvelope` →
`GrainInvocationRequest` format used by silo-to-silo TCP is reused as-is. No new protocol.

**Call flow for `player.Play("look")`:**
1. `TcpGatewayGrainFactory.GetGrain<IPlayerGrain>(guid)` returns a `PlayerGrainProxy` backed by `TcpGatewayCallInvoker`
2. `PlayerGrainProxy.Play("look")` creates `PlayerGrainProxy_PlayInvokable("look")`, calls `invoker.InvokeAsync<..., string?>`
3. `TcpGatewayCallInvoker` calls `invokable.Serialize(ref writer)` → argBytes → `GrainInvocationRequest` → `MessageEnvelope` → TCP
4. `GatewayMessagePump` reads envelope → `MessageDispatcher` → `PlayerGrain_TransportDispatcher.DispatchAsync` → deserializes args (factory passed for grain refs) → `LocalGrainCallInvoker` → `PlayerGrain.Play("look")`
5. Response travels back in reverse

---

## Changes by Package

### `Quark.Core.Abstractions`

**New: `IGrainProxy` interface**

```csharp
// Hosting/IGrainProxy.cs
public interface IGrainProxy
{
    GrainId GrainId { get; }
}
```

All generated proxy classes implement `IGrainProxy`, exposing their internal `_grainId` field. Used by the
grain-ref serializer to extract identity without boxing or reflection.

**Modified: `IGrainInvokable<T>`, `IGrainVoidInvokable`, and `IObserverVoidInvokable`**

Add `Serialize(ref CodecWriter writer)` to all three invokable interfaces. Observer invokables always emit a no-op body (observer calls are local-only and never travel over TCP, but the interface must be consistent):

```csharp
public interface IGrainInvokable<TResult>
{
    uint MethodId { get; }
    ValueTask<TResult> Invoke(Grain grain);
    void Serialize(ref CodecWriter writer);   // NEW
}

public interface IGrainVoidInvokable
{
    uint MethodId { get; }
    ValueTask Invoke(Grain grain);
    void Serialize(ref CodecWriter writer);   // NEW
}

public interface IObserverVoidInvokable
{
    uint MethodId { get; }
    ValueTask Invoke(object target);
    void Serialize(ref CodecWriter writer);   // NEW — always no-op body
}
```

Generated invokable structs already emit this method body — it just needs to be declared in the
interface so the TCP invoker can call it through the generic constraint without boxing.

**New dependency:** `Quark.Core.Abstractions` → `Quark.Serialization.Abstractions` (for `CodecWriter`).
Both are abstractions packages at the same architectural layer; serialization is a core distributed
systems concern.

**Impact on hand-written invokables** (`Quark.Tests.Unit`): add no-op
`public void Serialize(ref CodecWriter writer) { }` to all four `CounterGrain_*Invokable` structs.

---

### `Quark.CodeGenerator` (`GrainProxyGenerator`)

**4a. Generated proxies implement `IGrainProxy`**

Proxy class declaration gains `, global::Quark.Core.Abstractions.Hosting.IGrainProxy` and:
```csharp
public global::Quark.Core.Abstractions.Identity.GrainId GrainId => _grainId;
```

**4b. `SerializeKind.GrainRef` in `DetermineSerializeKind`**

Before the `Fallback` return, check if the type's interface hierarchy contains `IGrain`:
```
if type implements IGrain → SerializeKind.GrainRef
```

Key-type encoding on write (from `GetWriteExpr`):
```csharp
writer.WriteString(((global::Quark.Core.Abstractions.Hosting.IGrainProxy)_{param}).GrainId.Key);
```

Key-type reconstruction on read (from `GetReadExpr`) — the code generator knows the grain's key type
from its base interface (`IGrainWithIntegerKey`, `IGrainWithGuidKey`, `IGrainWithStringKey`) and
emits the matching `factory.GetGrain<T>()` overload:

| Base interface           | Read expression                                              |
|--------------------------|--------------------------------------------------------------|
| `IGrainWithIntegerKey`   | `factory!.GetGrain<T>(long.Parse(reader.ReadString()))`      |
| `IGrainWithGuidKey`      | `factory!.GetGrain<T>(Guid.ParseExact(reader.ReadString(), "N"))` |
| `IGrainWithStringKey`    | `factory!.GetGrain<T>(reader.ReadString())`                  |

**4c. Factory-aware `Deserialize`**

Signature change:
```csharp
public static {StructName} Deserialize(
    ref CodecReader reader,
    global::Quark.Core.Abstractions.Hosting.IGrainFactory? factory = null)
```

Invokables with no grain-ref parameters ignore `factory` — zero runtime cost. Only invokables with
at least one `GrainRef` parameter use it.

**4d. `ITransportGrainDispatcher` generated body**

Generated transport dispatchers call `Deserialize(ref reader, factory)`, passing the factory
received from `MessageDispatcher`.

---

### `Quark.Runtime`

**5a. `GatewayMessagePump` BackgroundService**

Near-clone of `SiloMessagePump` that listens on `SiloRuntimeOptions.GatewayAddress` (port 30000 by
default) instead of `SiloAddress`. `ProcessConnectionAsync` logic is identical. Both pumps share
the same `MessageDispatcher`.

Registered in `UseLocalhostClustering()`:
```csharp
builder.Services.AddHostedService<GatewayMessagePump>();
```

Silos that don't call `UseLocalhostClustering()` do not get this pump.

**5b. `IGrainFactory` injected into `MessageDispatcher`**

```csharp
public MessageDispatcher(
    TransportGrainDispatcherRegistry dispatcherRegistry,
    IGrainCallInvoker invoker,
    GrainMessageSerializer serializer,
    IGrainFactory grainFactory)    // NEW
```

`DispatchRequestAsync` forwards factory to transport dispatcher:
```csharp
result = await dispatcher.DispatchAsync(
    request.GrainId, request.MethodId, request.ArgumentPayload,
    _invoker, _grainFactory, cancellationToken);
```

**5c. `ITransportGrainDispatcher` interface change**

```csharp
Task<object?> DispatchAsync(
    GrainId grainId,
    uint methodId,
    ReadOnlyMemory<byte> argumentPayload,
    IGrainCallInvoker invoker,
    IGrainFactory factory,           // NEW
    CancellationToken cancellationToken = default);
```

Breaking change — any hand-written transport dispatcher (tests only) must add the factory parameter.

---

### `Quark.Client.Tcp` (new package)

**Dependencies:** `Quark.Client`, `Quark.Runtime`, `Quark.Transport.Tcp`

**`TcpGatewayClientOptions`**
```csharp
public class TcpGatewayClientOptions
{
    public IPEndPoint GatewayEndpoint { get; set; } = new(IPAddress.Loopback, 30000);
}
```

**`TcpGatewayConnection`**
- Holds `ITransportConnection`, `MessageSerializer`
- `ConcurrentDictionary<long, TaskCompletionSource<MessageEnvelope>> _pending`
- `long _nextCorrelationId` incremented via `Interlocked.Increment`
- `ConnectAsync(EndPoint, ct)` — calls `TcpTransport.ConnectAsync`, starts `ReadLoopAsync` as a background `Task`
- `SendAndAwaitAsync(envelope, ct)` — registers TCS in `_pending`, writes envelope to pipe, awaits TCS
- `ReadLoopAsync()` — reads envelopes, completes matching TCS by `CorrelationId`, faults all pending on disconnect
- `CloseAsync()` — cancels read loop, closes connection, faults all remaining pending

**`TcpGatewayCallInvoker : IGrainCallInvoker`**
- `InvokeAsync<TInvokable, TResult>`:
  1. `invokable.Serialize(ref argWriter)` → argBytes
  2. `grainMsgSerializer.SerializeRequest(new GrainInvocationRequest(grainId, invokable.MethodId, argBytes))`
  3. Wrap in `MessageEnvelope { MessageType.Request, correlationId, payload }`
  4. `_connection.SendAndAwaitAsync(envelope, ct)` → response envelope
  5. `grainMsgSerializer.DeserializeResponse(response.Payload)` → cast to `TResult`; throw on `!Success`
- `InvokeVoidAsync<TInvokable>` — same but discards result
- `InvokeObserverAsync` — throws `NotSupportedException` (observers are local-only)

**`TcpGatewayGrainFactory : IGrainFactory`**
- Same `GetGrain<T>` overloads as `LocalGrainFactory`
- Delegates to `GrainProxyFactoryRegistry` with `TcpGatewayCallInvoker` as the invoker

**`TcpGatewayClusterClient : IClusterClient`**
- `Connect(retryFilter)` → `_connection.ConnectAsync(options.GatewayEndpoint, ct)`
- `Close()` → `_connection.CloseAsync()`
- All `GetGrain` overloads delegate to `TcpGatewayGrainFactory`

**`TcpClientStartupService : IHostedService`**
- `StartAsync` → `client.Connect()`
- `StopAsync` → `client.Close()`

**`TcpClientBuilderExtensions`**
```csharp
// Orleans-compatible shorthand
public static IClientBuilder UseLocalhostGateway(this IClientBuilder builder, int gatewayPort = 30000);

// Generic
public static IClientBuilder UseTcpGateway(this IClientBuilder builder, Action<TcpGatewayClientOptions> configure);
```

Both extensions:
- Register `TcpGatewayClientOptions` via `Configure<>`
- Register `TcpGatewayConnection`, `TcpGatewayCallInvoker`, `TcpGatewayGrainFactory`, `TcpGatewayClusterClient`, `TcpClientStartupService`
- Remove any prior `LocalClusterClient` / `LocalGrainFactory` via `services.RemoveAll<>()`

---

## DI Wiring Example (AdventureClient)

```csharp
Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway();                   // connects to :30000
        client.Services.AddGrainProxy<IPlayerGrain, PlayerGrainProxy>();
        client.Services.AddGrainProxy<IRoomGrain, RoomGrainProxy>();
        client.Services.AddGrainProxy<IMonsterGrain, MonsterGrainProxy>();
    })
    .Build();
```

---

## Testing

**New test project:** `Quark.Tests.Gateway` (or new tests in `Quark.Tests.Integration`)

Tests use real TCP sockets on loopback — no mocking, no Testcontainers.

**Test 1 — basic round-trip**
- Silo: `UseLocalhostClustering()` + `IPingGrain` (one method: `Task<string> Ping(string msg)`)
- Client: `UseLocalhostGateway()` + `AddGrainProxy<IPingGrain, PingGrainProxy>()`
- Assert: `client.GetGrain<IPingGrain>("x").Ping("hello") == "hello"`

**Test 2 — grain-ref parameter round-trip**
- Silo: `ITrackerGrain.SetSource(ISourceGrain source)` + `Task<string> GetSourceName()`
- Client calls `tracker.SetSource(sourceProxy)` then `tracker.GetSourceName()`
- Assert: name matches

**Not in scope for F-15:**
- Reconnect / retry logic
- Client authentication
- Multi-silo routing from client (client always targets one gateway silo)
- TLS for gateway connections (covered by F-13 when needed)

---

## FEATURES.md Entry

```
## Phase 7 — Remote client

- [ ] **F-15** TCP gateway client (`TcpGatewayClusterClient`, `UseLocalhostGateway()`,
  `GatewayMessagePump`, grain-ref serialization) — _Complexity: XL_
```
