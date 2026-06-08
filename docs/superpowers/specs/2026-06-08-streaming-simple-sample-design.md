# Design: Streaming/Simple Sample

**Issue:** #14 — Add Streaming sample  
**Date:** 2026-06-08  
**Status:** Approved

## Goal

Port the Orleans `Streaming/Simple` sample to Quark, demonstrating in-memory stream publishing from a producer grain (via a timer), explicit grain-side subscription from a consumer grain, and client-side direct subscription — all over a TCP gateway.

## Deviation from Orleans original

Orleans' `ConsumerGrain` uses `[ImplicitStreamSubscription]` + `IStreamSubscriptionObserver.OnSubscribed` to auto-activate when a matching stream has activity. Quark defines both the attribute and the interface but does not yet wire them in the runtime. The sample uses **explicit subscription** instead: `IConsumerGrain` exposes `Subscribe(StreamId)` / `Unsubscribe()`, and the client calls these methods after starting the producer.

## Project layout

```
samples/Streaming/
  Streaming.Common/
    Constants.cs              — StreamProvider name, StreamNamespace string
  Streaming.Simple/
    Streaming.Simple.GrainInterfaces/
      IProducerGrain.cs       — IGrainWithStringKey; StartProducing / StopProducing
      IConsumerGrain.cs       — IGrainWithGuidKey;  Subscribe / Unsubscribe
      AssemblyInfo.cs         — global usings
    Streaming.Simple.Grains/
      ProducerBehavior.cs     — publishes int via IGrainTimer
      ConsumerBehavior.cs     — holds StreamSubscriptionHandle<int> in activation memory
      AssemblyInfo.cs
    Streaming.Simple.SiloHost/
      Program.cs              — silo + local client wiring, port 30003
    Streaming.Simple.Client/
      Program.cs              — TCP client, subscribes from both grain and client side
```

Four `.csproj` files added; `Quark.slnx` updated with all four.

## Interfaces

```csharp
// IProducerGrain — identical to Orleans original
public interface IProducerGrain : IGrainWithStringKey
{
    Task StartProducing(string ns, Guid key);
    Task StopProducing();
}

// IConsumerGrain — adds explicit Subscribe/Unsubscribe (replaces implicit activation)
public interface IConsumerGrain : IGrainWithGuidKey
{
    Task Subscribe(StreamId streamId);
    Task Unsubscribe();
}
```

## Behaviors

### `ProducerBehavior`

- Injects: `IActivationShellAccessor` (for `Shell.RegisterTimer`), `ICallContext`, `[FromKeyedServices("simple")] IStreamProvider`
- `StartProducingAsync`: resolves `IAsyncStream<int>` from provider, registers a 1-second `IGrainTimer` that increments a counter and calls `stream.OnNextAsync(value)`. Throws if already producing.
- `StopProducingAsync`: disposes the timer, nulls the stream reference.
- State lives in `ProducerState` (held in `IActivationMemory<ProducerState>`): `IAsyncStream<int>?`, `IGrainTimer?`, `int Counter`.

### `ConsumerBehavior`

- Injects: `IActivationMemory<ConsumerState>`, `[FromKeyedServices("simple")] IStreamProvider`, `ILogger<IConsumerGrain>`
- `Subscribe(streamId)`: resolves stream, calls `stream.SubscribeAsync(new LoggerObserver(_logger))`, stores the returned `StreamSubscriptionHandle<int>` in state.
- `Unsubscribe()`: calls `handle.UnsubscribeAsync()`, nulls it.
- `LoggerObserver` (private nested class): implements `IAsyncObserver<int>`, logs each item via `ILogger`.
- State: `ConsumerState` holds `StreamSubscriptionHandle<int>?`.

**Note:** `ConsumerState` cannot be `[GenerateSerializer]`-tagged because `StreamSubscriptionHandle<int>` is not serializable. It is activation memory only (never crosses a wire).

## Silo wiring (`SiloHost/Program.cs`)

```csharp
Host.CreateDefaultBuilder(args)
    .UseQuark(silo => {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30003);
        silo.Services.AddMemoryStreams("simple");
        // int is a Quark primitive codec — AddStreamableCodec<int,...> only needed if
        // InMemory provider requires explicit registration; confirm during implementation.
        silo.Services.AddGrainBehavior<IProducerGrain, ProducerBehavior>();
        silo.Services.AddGrainBehavior<IConsumerGrain, ConsumerBehavior>();
        silo.Services.AddGrainTransportDispatcher(new GrainType("ProducerGrain"), new ProducerGrainProxy_TransportDispatcher());
        silo.Services.AddGrainTransportDispatcher(new GrainType("ConsumerGrain"), new ConsumerGrainProxy_TransportDispatcher());
        // IActivationMemory<ProducerState> + IActivationMemory<ConsumerState> scoped accessors
    })
    .UseQuarkClient(client => {
        client.Services.AddLocalClusterClient();
        client.Services.AddGrainProxy<IProducerGrain, ProducerGrainProxy>();
        client.Services.AddGrainProxy<IConsumerGrain, ConsumerGrainProxy>();
    })
```

## Client wiring (`Client/Program.cs`)

```csharp
Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client => {
        client.UseLocalhostGateway(30003);
        client.AddTcpClientStreams("simple");
        client.Services.AddGrainProxy<IProducerGrain, ProducerGrainProxy>();
        client.Services.AddGrainProxy<IConsumerGrain, ConsumerGrainProxy>();
    })
```

Client flow:
1. `producer.StartProducing(Constants.StreamNamespace, key)` — producer grain starts timer
2. `consumer.Subscribe(StreamId.Create(ns, key))` — consumer grain subscribes (explicit, replaces implicit)
3. Client subscribes directly: `stream.SubscribeAsync((item, _) => { Console.WriteLine(...); return Task.CompletedTask; })`
4. `await Task.Delay(Timeout.Infinite, cts.Token)` — run until Ctrl+C
5. `producer.StopProducing()` + `consumer.Unsubscribe()`

## Port 30003

ChatRoom.Server uses 30002. This sample uses 30003 to avoid collisions when both samples run simultaneously.

## Items to verify during implementation

- Whether `AddStreamableCodec<int, ...>` is required for in-memory streams (int may be handled by the built-in codec provider already).
- Whether `GrainType("ProducerGrain")` / `GrainType("ConsumerGrain")` match the generator-emitted dispatcher type names (follow ChatRoom pattern).
- The `Quark.slnx` solution file format (check existing entries to match exactly).
