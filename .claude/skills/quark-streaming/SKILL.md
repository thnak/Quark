---
name: quark-streaming
description: Use when adding Quark streaming — publishing from a grain (IAsyncStream/OnNextAsync), subscribing grain-side or client-side (IAsyncObserver/SubscribeAsync), registering stream item codecs (AddStreamableCodec), implicit subscriptions, or TCP client stream push (AddTcpClientStreams). Quark-specific.
---

# Quark Streaming

## Overview

A stream is identified by a `StreamId` (namespace + key) and typed by its item `T`. You get streams from an `IStreamProvider` (named, resolved by DI key). Publish with `OnNextAsync`; subscribe with an `IAsyncObserver<T>` or a delegate. Stream item types that cross TCP need a registered codec via `AddStreamableCodec<T, TCodec>()`.

## Quick reference

| Action | Code |
|---|---|
| Register provider (silo) | `silo.Services.AddMemoryStreams("name");` |
| Register item codec | `services.AddStreamableCodec<MyMsg, MyMsgCodec>();` |
| Get provider in a behavior | inject `[FromKeyedServices("name")] IStreamProvider? p` |
| Get a stream | `p.GetStream<MyMsg>(StreamId.Create("ns", key))` |
| Publish | `await stream.OnNextAsync(msg);` |
| Subscribe (observer) | `var handle = await stream.SubscribeAsync(observer);` |
| Unsubscribe | `await handle.UnsubscribeAsync();` |
| Get provider on client | `clusterClient.GetStreamProvider("name")` |
| Client stream push (TCP) | `client.AddTcpClientStreams("name");` |

## Publishing from a grain

```csharp
public sealed class ChannelBehavior : IGrainBehavior, IChannelGrain, IActivationLifecycle
{
    private readonly IActivationMemory<ChannelState> _memory;
    private readonly ICallContext _ctx;
    private readonly IStreamProvider? _provider;

    public ChannelBehavior(
        IActivationMemory<ChannelState> memory,
        ICallContext ctx,
        [FromKeyedServices("chat")] IStreamProvider? provider = null)   // keyed by provider name
    {
        _memory = memory; _ctx = ctx; _provider = provider;
    }

    private ChannelState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct)
    {
        if (_provider is not null && S.Stream is null)
            S.Stream = _provider.GetStream<ChatMsg>(StreamId.Create("ChatRoom", _ctx.GrainId.Key));
        return Task.CompletedTask;
    }
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> Message(ChatMsg msg)
    {
        if (S.Stream is not null) await S.Stream.OnNextAsync(msg);
        return true;
    }
}
```

## Subscribing grain-side

```csharp
public async Task Subscribe(StreamId streamId)
{
    IAsyncStream<int> stream = _provider!.GetStream<int>(streamId);
    S.Handle = await stream.SubscribeAsync(new LoggingObserver(_logger));   // store handle in activation memory
}
public async Task Unsubscribe()
{
    if (S.Handle is not null) { await S.Handle.UnsubscribeAsync(); S.Handle = null; }
}

private sealed class LoggingObserver(ILogger logger) : IAsyncObserver<int>
{
    public Task OnNextAsync(int item, StreamSequenceToken? token = null) { /* ... */ return Task.CompletedTask; }
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    public Task OnCompletedAsync() => Task.CompletedTask;
}
```

## Subscribing client-side (over the TCP gateway)

```csharp
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30002);
    client.AddTcpClientStreams("chat");                       // enables silo→client push
    client.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
    client.Services.AddMyInterfacesGrainProxies();
})
...
var provider = clusterClient.GetStreamProvider("chat");
var streamId = await grain.Join(user);                        // grain returns the StreamId to watch
var handle  = await provider.GetStream<ChatMsg>(streamId).SubscribeAsync(new StreamObserver(channel));
// later: await handle.UnsubscribeAsync();
```

## Implicit subscriptions

`silo.Services.AddImplicitStreamSubscription("namespace", "GrainTypeKey")` auto-activates the matching grain (key = stream key) when a stream under that namespace gets activity. The grain still subscribes itself in `OnActivateAsync`; auto-activation just guarantees the first item isn't lost.

## Common mistakes

- **Missing codec for the item type** → publish/subscribe fails over TCP. Register `AddStreamableCodec<T, TCodec>()` on **both** silo and the clients/silos that carry it.
- **Provider name mismatch** between `AddMemoryStreams("x")` and `[FromKeyedServices("x")]` / `GetStreamProvider("x")`.
- **Losing the subscription handle.** Store `StreamSubscriptionHandle<T>` in activation memory so you can unsubscribe.
- **Client push without `AddTcpClientStreams`** — without it the client never receives silo-pushed items.

## Related skills

- quark-writing-grains — the behavior/state/DI scaffold these snippets live in
- quark-host-setup — provider + codec registration on silo and client
