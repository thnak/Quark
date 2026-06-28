# Streaming

Quark implements Orleans-compatible streams via `IAsyncStream<T>`. Two transport modes are available: **in-memory** (silo-side pub/sub) and **TCP client streams** (pushed from silo to remote clients).

## Core interfaces

```csharp
// Get a stream from the named provider
IStreamProvider provider = clusterClient.GetStreamProvider("chat");

// Obtain a typed stream by id
IAsyncStream<ChatMsg> stream = provider.GetStream<ChatMsg>(streamId);

// Publish
await stream.OnNextAsync(new ChatMsg { ... });

// Subscribe
StreamSubscriptionHandle<ChatMsg> handle =
    await stream.SubscribeAsync(observer);

// Unsubscribe
await handle.UnsubscribeAsync();
```

## Stream identity

A `StreamId` identifies a stream within a provider:

```csharp
// Create by namespace + key
StreamId id = StreamId.Create("ChatRoom", grainId.Key);
StreamId id = StreamId.Create("Notifications", userId.ToString());
```

The namespace is a logical grouping; the key is the unique identifier within that namespace.

## In-memory stream provider

Suitable for single-silo scenarios or co-hosted server+client. The in-memory provider delivers messages synchronously within the same silo process.

### Server registration

```csharp
silo.Services.AddMemoryStreams("chat");
silo.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
```

### Client registration (co-hosted)

```csharp
// The local cluster client automatically has access to the silo's stream provider
var provider = clusterClient.GetStreamProvider("chat");
```

### Observer implementation

```csharp
public sealed class StreamObserver : IAsyncObserver<ChatMsg>
{
    public Task OnNextAsync(ChatMsg item, StreamSequenceToken? token = null)
    {
        Console.WriteLine($"[{item.Author}] {item.Text}");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        Console.Error.WriteLine($"Stream error: {ex.Message}");
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;
}
```

## Implicit stream subscriptions

Mark a grain behavior with `[ImplicitStreamSubscription]` to have it automatically subscribed to all streams in a given namespace whose key matches the grain's primary key:

```csharp
[ImplicitStreamSubscription("Alerts")]
public sealed class AlertProcessorBehavior : IGrainBehavior, IAlertProcessorGrain,
    IAsyncObserver<AlertMsg>
{
    public Task OnNextAsync(AlertMsg item, StreamSequenceToken? token = null)
    {
        // Called for every message on the "Alerts/{this-grain-key}" stream
        return ProcessAlertAsync(item);
    }

    public Task OnErrorAsync(Exception ex)     => Task.CompletedTask;
    public Task OnCompletedAsync()             => Task.CompletedTask;
}
```

The runtime resolves the subscription when the matching stream receives its first message: the
in-memory provider looks the namespace up in the `ImplicitStreamSubscriptionRegistry`, then asks
the silo's `LocalImplicitStreamActivator` to activate the grain whose key equals the stream key and
deliver the message.

### Registration

Each namespace → grain-type mapping must be registered on the silo. The
`BehaviorRegistrationGenerator` does this automatically: every `[ImplicitStreamSubscription("ns")]`
emits an `AddImplicitStreamSubscription("ns", "GrainType")` call into `AddMyAssemblyBehaviors()`,
so no manual wiring is needed.

```csharp
silo.Services.AddMemoryStreams("Alerts");
silo.Services.AddMyAssemblyBehaviors();   // auto-registers the implicit subscription
```

To wire it by hand (or for a behavior outside the generated assembly):

```csharp
silo.Services.AddImplicitStreamSubscription("Alerts", "AlertProcessor");
```

> The generator emits this call only when the assembly references `Quark.Streaming.InMemory`.
> Otherwise it skips emission and reports diagnostic **QRK0023** — add the reference to enable
> auto-wiring. See [Source Generators](Source-Generators#implicit-stream-subscriptions).

## TCP client streams (`Quark.Client.Tcp`)

Allows remote TCP clients to subscribe to silo streams and receive pushed messages over the same TCP connection used for grain calls.

### Server-side setup

Nothing extra needed — streams are published normally. The `GatewayMessagePump` handles fan-out to subscribed TCP clients.

### Client-side setup

```csharp
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30002);
    client.AddTcpClientStreams("chat");                          // name must match silo provider
    client.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>(); // same codec as server
    client.Services.AddGrainProxy<IChannelGrain, ChannelGrainProxy>();
})
```

### Subscribing from a remote client

```csharp
var clusterClient = host.Services.GetRequiredService<IClusterClient>();
var streamProvider = clusterClient.GetStreamProvider("chat");

// Obtain stream id from the grain
var grain = clusterClient.GetGrain<IChannelGrain>("general");
var streamId = await grain.Join("alice");

// Subscribe — messages arrive pushed over TCP
var stream = streamProvider.GetStream<ChatMsg>(streamId);
var handle = await stream.SubscribeAsync(new ChatMsgObserver());
```

When the remote client subscribes, the silo registers a `GatewayClientSubscription`. Whenever the grain publishes to the stream, the `GatewayMessagePump` serializes the message and pushes it over the client's TCP connection.

### Unsubscribing

```csharp
await handle.UnsubscribeAsync();
```

## Stream sequence tokens

`StreamSequenceToken` carries a sequence number and event index for ordering guarantees. Pass it to `SubscribeAsync` to resume from a known position:

```csharp
await stream.SubscribeAsync(observer, token: lastToken);
```

The in-memory provider assigns monotonically increasing sequence numbers per stream.

## ChatRoom sample

The [ChatRoom sample](Samples#chatroom) demonstrates the full pattern: server-side grain publishes to a stream, a remote TCP client subscribes and receives pushed messages in real time.
