# F-16 Client-Side TCP Streaming + ChatRoom Sample

**Date:** 2026-06-06
**Status:** Approved

## Overview

Extend Quark's TCP gateway protocol to support server→client stream push, then port the Orleans ChatRoom sample to demonstrate the feature end-to-end.

Two deliverables:
1. **F-16**: Client-side streaming infrastructure — clients subscribe to Quark streams over the existing TCP gateway connection and receive pushed items in real time.
2. **ChatRoom sample**: Console multiplayer chat (`samples/ChatRoom/`) that showcases streams the same way the Orleans original does.

## Scope

### In scope
- Three new `MessageType` wire values (StreamSubscribe, StreamUnsubscribe, StreamPush)
- `IUntypedStreamSubscriptionRegistry` + `IUntypedStreamObserver` in `Quark.Streaming.Abstractions`
- Untyped subscriber hook in `StreamSubscriptionRegistry` for gateway delivery
- Per-connection subscription tracking and cleanup in `GatewayMessagePump`
- `TcpClientStreamProvider` / `TcpClientStream<T>` / `TcpStreamSubscriptionHandle<T>` in `Quark.Client.Tcp`
- `IClusterClient.GetStreamProvider(name)` method
- `AddTcpClientStreams(name)` DI extension on client builder
- ChatRoom sample (Server + Client + Common, port 30002, Spectre.Console UI)

### Out of scope
- Cross-silo stream delivery
- Durable stream providers (Kafka, Service Bus)
- Client-side stream publishing (`OnNextAsync` from client throws `NotSupportedException`)
- Persistent pub/sub subscription state (subscriptions last only for the connection lifetime)

---

## Section 1 — Protocol

### New `MessageType` values

```csharp
// Quark.Transport.Abstractions/MessageType.cs
public enum MessageType : byte
{
    Request        = 0,
    Response       = 1,
    OneWayRequest  = 2,
    System         = 3,
    StreamSubscribe   = 4,   // client → server
    StreamUnsubscribe = 5,   // client → server (one-way)
    StreamPush        = 6,   // server → client (unsolicited)
}
```

### Wire format per message type

All messages use the existing `MessageEnvelope` framing (4-byte length prefix + envelope).

**StreamSubscribe (4)** — client sends this message type; server processes it and replies with a `MessageType.Response (1)` ack using the same `CorrelationId`:
- `CorrelationId`: next client sequence number (matched to ack response by existing `TcpGatewayConnection` read loop)
- Headers: `stream-ns` (string), `stream-key` (string), `sub-id` (Guid as `"D"` format string)
- Payload: empty

**StreamUnsubscribe (5)** — client sends; no response expected:
- `CorrelationId`: 0
- Headers: `sub-id`
- Payload: empty

**StreamPush** (unsolicited, server → client):
- `CorrelationId`: -1 (signals "not a response")
- Headers: `stream-ns`, `stream-key`, `sub-id`, `seq` (sequence number as string)
- Payload: item serialized by `IGeneralizedCodec` using the item's concrete runtime type

No codec type identifier is carried in the headers. The server serializes using `item.GetType()` and the registered `IGeneralizedCodec`; the client deserializes using the `IFieldCodec<T>` it registered when calling `GetStream<T>()`.

---

## Section 2 — Server-Side Infrastructure

### `Quark.Streaming.Abstractions` (new interface)

**`IUntypedStreamSubscriptionRegistry`** (public interface):
```csharp
public interface IUntypedStreamSubscriptionRegistry
{
    Guid SubscribeUntyped(StreamId streamId, IUntypedStreamObserver observer);
    void UnsubscribeUntyped(Guid subId);
}
```

`InMemoryStreamProvider` (and its internal `StreamSubscriptionRegistry`) implement this interface. `GatewayMessagePump` depends on `IUntypedStreamSubscriptionRegistry` resolved from DI, avoiding any direct reference to `Quark.Streaming.InMemory`.

`IUntypedStreamObserver` is also moved to `Quark.Streaming.Abstractions` (public) so `Quark.Runtime` can reference it without depending on `Quark.Streaming.InMemory`.

### `Quark.Streaming.InMemory`

**`IUntypedStreamObserver`** (now in `Quark.Streaming.Abstractions`, public):
```csharp
internal interface IUntypedStreamObserver
{
    Task OnNextAsync(object item, StreamSequenceToken? token);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();
}
```

**`StreamSubscriptionRegistry`** additions:
- `Guid SubscribeUntyped(StreamId streamId, IUntypedStreamObserver observer)` — adds to a separate `ConcurrentDictionary<StreamId, List<UntypedSubscription>>` keyed by `Guid` subscription ID; returns the generated ID.
- `void UnsubscribeUntyped(Guid subId)` — removes by subscription ID from the untyped list.
- `PublishAsync<T>` iteration extended: after calling typed subscribers, iterate the untyped list for the same `StreamId`, boxing `item` as `object` and calling `observer.OnNextAsync(item, token)`.

Errors from untyped subscribers are aggregated alongside typed-subscriber errors in the existing `AggregateException` path.

### `Quark.Runtime`

**`GatewayClientSubscription`** (implements `IUntypedStreamObserver`):
- Fields: `Guid SubId`, `StreamId StreamId`, `Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task> Push`
- `OnNextAsync(object item, token)`: call `IGeneralizedCodec.WriteField` to serialize `item` to a `CodecWriter`, build a `StreamPush` envelope, call `Push(bytes, token)`.
- `OnErrorAsync` / `OnCompletedAsync`: no-op for now (connection close handles cleanup).

**`GatewayClientSubscriptionTable`** (singleton service):
- `ConcurrentDictionary<Guid, GatewayClientSubscription> _all`
- `Add(GatewayClientSubscription)` / `Remove(Guid subId)` / `RemoveAll(IEnumerable<Guid> subIds)` for connection-close cleanup.

**`GatewayMessagePump`** changes:
- On accept: create a `List<Guid> connectionSubscriptions` per connection to track all sub-IDs for that connection.
- Handle `MessageType.StreamSubscribe`:
  1. Parse `stream-ns`, `stream-key`, `sub-id` from headers.
  2. Construct a `Push` delegate that serializes and writes a `StreamPush` envelope to the connection's `PipeWriter` (under the existing write lock).
  3. Create `GatewayClientSubscription` and register in both `StreamSubscriptionRegistry.SubscribeUntyped` and `GatewayClientSubscriptionTable`.
  4. Add `sub-id` to `connectionSubscriptions`.
  5. Return an empty `MessageType.Response` envelope (ack).
- Handle `MessageType.StreamUnsubscribe`:
  1. Parse `sub-id`.
  2. Call `StreamSubscriptionRegistry.UnsubscribeUntyped(subId)` and `GatewayClientSubscriptionTable.Remove(subId)`.
  3. Return null (one-way).
- On connection close: call `StreamSubscriptionRegistry.UnsubscribeUntyped` + `GatewayClientSubscriptionTable.Remove` for every ID in `connectionSubscriptions`.

---

## Section 3 — Client-Side Infrastructure (`Quark.Client.Tcp`)

### Internal types

**`IClientStreamSubscription`** (type-erased):
```csharp
internal interface IClientStreamSubscription
{
    StreamId StreamId { get; }
    Task DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token);
    Task ErrorAsync(Exception ex);
}
```

**`TcpClientStreamSubscription<T>`** implements `IClientStreamSubscription`:
- Fields: `Guid SubId`, `IAsyncObserver<T> Observer`, `IFieldCodec<T> Codec`
- `DispatchAsync`: decode payload via `CodecReader` + `Codec`, call `Observer.OnNextAsync(item, token)`.

**`TcpStreamPushDispatcher`** (singleton per client connection):
- `ConcurrentDictionary<Guid, IClientStreamSubscription> _subscriptions`
- `Register(Guid subId, IClientStreamSubscription sub)` / `Unregister(Guid subId)`
- `IReadOnlyList<IClientStreamSubscription> GetForStream(StreamId streamId)` — filters by `StreamId`
- `DispatchAsync(MessageEnvelope envelope)`:
  1. Parse `sub-id`, `seq` from headers; parse `stream-ns`, `stream-key`.
  2. Look up subscription by `sub-id`.
  3. Create `SequentialToken(seq)`.
  4. Call `subscription.DispatchAsync(envelope.Payload, token)`.

### `TcpGatewayConnection` changes

`ReadLoopAsync`: when an incoming envelope has `CorrelationId == -1` (or `MessageType.StreamPush`), route to `_pushDispatcher.DispatchAsync(envelope)` instead of matching `_pending`.

### Public types

**`TcpStreamSubscriptionHandle<T>`** extends `StreamSubscriptionHandle<T>`:
- `UnsubscribeAsync()`: send `StreamUnsubscribe` one-way, call `_dispatcher.Unregister(HandleId)`.
- `ResumeAsync(observer, token)`: replace the observer on the existing `TcpClientStreamSubscription<T>`.

**`TcpClientStream<T>`** implements `IAsyncStream<T>`:
- `SubscribeAsync(IAsyncObserver<T> observer)`:
  1. Generate `subId = Guid.NewGuid()`.
  2. Send `StreamSubscribe` Request (with headers), await ack Response.
  3. Create `TcpClientStreamSubscription<T>`, register in dispatcher.
  4. Return `TcpStreamSubscriptionHandle<T>`.
- `GetAllSubscriptionHandles()`: return handles from `_dispatcher.GetForStream(StreamId)`.
- `OnNextAsync` / `OnErrorAsync` / `OnCompletedAsync`: throw `NotSupportedException` ("Clients cannot publish to streams").

**`TcpClientStreamProvider`** implements `IStreamProvider`:
- `ConcurrentDictionary<(StreamId, Type), object> _streams` — lazily creates typed `TcpClientStream<T>`.
- `GetStream<T>(StreamId)` → returns (or creates) `TcpClientStream<T>`.

### `IClusterClient` changes

```csharp
public interface IClusterClient : IGrainFactory
{
    IStreamProvider GetStreamProvider(string name);   // new
}
```

`TcpGatewayClusterClient.GetStreamProvider(name)` resolves the keyed `IStreamProvider` from DI.

### DI registration

```csharp
// Client builder extension (Quark.Client.Tcp)
public static IClientBuilder AddTcpClientStreams(
    this IClientBuilder builder, string providerName)
{
    builder.Services.AddKeyedSingleton<IStreamProvider>(providerName,
        (sp, _) => new TcpClientStreamProvider(
            providerName,
            sp.GetRequiredService<TcpGatewayConnection>(),
            sp.GetRequiredService<TcpStreamPushDispatcher>(),
            sp.GetRequiredService<IQuarkSerializer>()));
    return builder;
}
```

`TcpStreamPushDispatcher` registered as a singleton automatically when `AddTcpGatewayClient` is called.

---

## Section 4 — ChatRoom Sample

### Project structure

```
samples/ChatRoom/
  ChatRoom.slnx
  ChatRoom.Common/
    ChatRoom.Common.csproj    (references Quark.Core.Abstractions, Quark.Serialization.Abstractions)
    IChannelGrain.cs
    ChatMsg.cs
  ChatRoom.Server/
    ChatRoom.Server.csproj    (references ChatRoom.Common, Quark.Runtime, Quark.Transport.Tcp, Quark.Streaming.InMemory)
    ChannelGrain.cs
    Program.cs
  ChatRoom.Client/
    ChatRoom.Client.csproj    (references ChatRoom.Common, Quark.Client.Tcp, Spectre.Console)
    StreamObserver.cs
    Program.cs
```

Gateway port: **30002** (Adventure uses 30001).

### `ChatRoom.Common`

```csharp
// IChannelGrain.cs
public interface IChannelGrain : IGrainWithStringKey
{
    Task<StreamId> Join(string nickname);
    Task<StreamId> Leave(string nickname);
    Task<bool> Message(ChatMsg msg);
    Task<ChatMsg[]> ReadHistory(int numberOfMessages);
    Task<string[]> GetMembers();
}

// ChatMsg.cs
[GenerateSerializer, Alias("ChatMsg")]
public record ChatMsg(
    [property: Id(0)] string Author,
    [property: Id(1)] string Text,
    [property: Id(2)] DateTimeOffset Created);
```

### `ChatRoom.Server`

**`ChannelGrain`** — nearly identical to the Orleans original:
- On `OnActivateAsync`: get stream provider `"chat"`, create `StreamId.Create("ChatRoom", primaryKey)`, store `IAsyncStream<ChatMsg>`.
- `Join`: add member, publish system message, return `StreamId`.
- `Leave`: remove member, publish system message, return `StreamId`.
- `Message`: append to history (cap at 100), publish, return true.
- `GetMembers` / `ReadHistory`: read in-memory state.

**`Program.cs`**:
```csharp
builder.UseQuark(silo => {
    silo.Services.AddQuarkRuntime();
    silo.Services.AddTcpTransport();
    silo.UseLocalhostClustering(gatewayPort: 30002);
    silo.Services.AddMemoryStreams("chat");
    // ChannelGrain + generated invoker + activator registrations
});
```

No extra registration needed for gateway streaming — the `GatewayMessagePump` handles StreamSubscribe/StreamUnsubscribe automatically once the stream provider is present.

### `ChatRoom.Client`

**`StreamObserver`** (implements `IAsyncObserver<ChatMsg>`):
- `OnNextAsync`: print `[HH:mm:ss][channel] Author: Text` via Spectre.Console.
- `OnErrorAsync`: print exception.
- `OnCompletedAsync`: no-op.

**`Program.cs`** flow:
1. Connect: `TcpGatewayClusterClient.ConnectAsync("localhost", 30002)`.
2. Register stream provider: `client.GetStreamProvider("chat")`.
3. Prompt for username.
4. REPL:
   - `/j <channel>` → `grain.Join(username)` → subscribe stream → print joined.
   - `/l` → `grain.Leave(username)` → unsubscribe all handles → print left.
   - `/h` → `grain.ReadHistory(1000)` → print history.
   - `/m` → `grain.GetMembers()` → print table.
   - `/n <name>` → change username.
   - `/exit` → disconnect and quit.
   - `<text>` → `grain.Message(new ChatMsg(username, text, DateTimeOffset.UtcNow))`.

UI uses Spectre.Console (same dependency as the Orleans original).

---

## Build & Run

```bash
# Terminal 1
dotnet run --project samples/ChatRoom/ChatRoom.Server

# Terminal 2 (and more)
dotnet run --project samples/ChatRoom/ChatRoom.Client
```

Multiple client processes can connect and all receive the same stream messages in real time.

---

## Testing

Unit tests for the new wire protocol go in `Quark.Tests.Unit`:
- `StreamSubscribeDispatchTests` — server correctly registers/unregisters untyped subscriptions on receiving StreamSubscribe/StreamUnsubscribe messages.
- `TcpStreamPushDispatcherTests` — client correctly deserializes and routes StreamPush envelopes.

Integration test in `Quark.Tests.Integration`:
- `ClientStreamingIntegrationTests` — in-process silo + `TcpGatewayClusterClient`; grain publishes to stream; client receives push; client unsubscribes; no further messages received.

---

## Implementation Order

1. Protocol (`MessageType` enum) — 1 file change
2. `IUntypedStreamObserver` + `IUntypedStreamSubscriptionRegistry` in `Quark.Streaming.Abstractions` — 2 new files
3. `StreamSubscriptionRegistry` untyped hook — 2 file changes
4. `GatewayClientSubscription` + `GatewayClientSubscriptionTable` — 2 new files
5. `GatewayMessagePump` — StreamSubscribe/StreamUnsubscribe/connection-close handling
6. `TcpStreamPushDispatcher` — new file in `Quark.Client.Tcp`
7. `TcpClientStream<T>` + `TcpClientStreamProvider` + `TcpStreamSubscriptionHandle<T>` + `TcpClientStreamSubscription<T>` — 4 new files
8. `TcpGatewayConnection` read-loop patch — 1 file change
9. `IClusterClient` + `TcpGatewayClusterClient` + `AddTcpClientStreams` DI — 3 file changes
10. Unit tests
11. Integration test
12. ChatRoom sample (Common → Server → Client)
