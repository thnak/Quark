# Architecture

## Package layout

| Package | Role |
|---|---|
| `Quark.Core.Abstractions` | `GrainId`, `GrainType`, `IGrain`, key-typed grain interfaces, `IGrainFactory`, `IClusterClient`, `IGrainContext`, lifecycle, placement attributes |
| `Quark.Serialization.Abstractions` | `IFieldCodec<T>`, `IDeepCopier<T>`, `CodecWriter`/`CodecReader`, `[GenerateSerializer]`/`[Id]`/`[Alias]` |
| `Quark.Transport.Abstractions` | `ITransport`, `ITransportListener`, `ITransportConnection` (IDuplexPipe), `MessageEnvelope` |
| `Quark.Core` | `ISiloBuilder`, `IClientBuilder`, `UseQuark()`/`UseQuarkClient()` host-builder extensions |
| `Quark.Runtime` | Silo-side runtime: `GrainActivation`, `GrainActivationTable`, `LocalGrainCallInvoker`, `SiloHostedService`, message pump/dispatcher, placement |
| `Quark.Client` | `LocalClusterClient`, `LocalGrainFactory`, proxy/observer factory registries |
| `Quark.Client.Tcp` | `TcpGatewayClusterClient`, `TcpGatewayGrainFactory`, client-side stream push |
| `Quark.Serialization` | 18 primitive codecs, `CodecProvider`, `QuarkSerializer`, serialization DI |
| `Quark.Transport.Tcp` | `TcpTransport`/`TcpTransportListener`/`TcpTransportConnection` (System.IO.Pipelines), TLS |
| `Quark.Persistence.Abstractions` | `IGrainStorage`, `IPersistentActivationMemory<T>`, `IStorage<T>`, `GrainState<T>`, `JournaledGrain<TState,TEvent>` |
| `Quark.Persistence.InMemory` | In-memory `IGrainStorage` provider |
| `Quark.Persistence.Redis` | Redis-backed `IGrainStorage` (StackExchange.Redis) |
| `Quark.Reminders.Abstractions` | `IRemindable`, `IReminderService`, `IGrainReminder`, `DefaultReminderService` |
| `Quark.Reminders.InMemory` | In-process reminder store |
| `Quark.Reminders.Redis` | Redis-backed reminder store |
| `Quark.Streaming.Abstractions` | `IAsyncStream<T>`, `IAsyncObserver<T>`, `StreamId`, `[ImplicitStreamSubscription]` |
| `Quark.Streaming.InMemory` | In-memory stream provider |
| `Quark.Transactions` | `ITransactionalState<T>`, `[Transaction]`, 2PC coordinator |
| `Quark.CodeGenerator` | Roslyn incremental generators: `GrainProxyGenerator`, `BehaviorRegistrationGenerator`, `SerializerGenerator` |
| `Quark.Analyzers` | AOT-safety Roslyn analyzers (QRK0001–QRK0003) |
| `Quark.Testing` | `TestCluster`/`TestSilo`/`TestClient` in-process test harness |

## Engine model (M2)

Quark's M2 milestone moved from the Orleans **Framework model** (inheriting `Grain`) to the **Engine model** (implementing `IGrainBehavior`).

| | Framework model | Engine model |
|---|---|---|
| Developer writes | `class MyGrain : Grain` | `class MyBehavior : IGrainBehavior` |
| Lifecycle owner | Developer (inherits base class) | Engine (controls scope and scheduling) |
| DI scope | Root — leaks scoped services | Per-call — fresh `IServiceScope` each call |
| In-memory state | Field on the long-lived grain | `IActivationMemory<TState>` — lives in shell |
| Persistent state | `Grain<TState>` base class | `IPersistentActivationMemory<TState>` |
| RAM footprint | Full grain object tree per activation | Lightweight shell; behavior objects exist for milliseconds |
| Startup safety | Fails at first live call | `BehaviorStartupValidator` fails silo at startup |

### Key concepts

**Shell** (`GrainActivation`) — the long-lived object that owns the mailbox (`Channel<Func<Task>>`). It holds the `GrainId`, root `IServiceProvider`, and `StateHolder<TState>` bags for activation memory. One shell per live grain identity on this silo.

**Behavior** — a POCO class implementing `IGrainBehavior`. Constructed per call inside a short-lived `IServiceScope`, executes, then discarded. All constructor parameters are resolved from that scope.

**Activation memory** — a `StateHolder<TState>` owned by the shell. Survives across calls on the same activation; lost on deactivation. Exposed to the behavior via `IActivationMemory<TState>`.

## Local call flow

```
IClusterClient.GetGrain<IMyGrain>(key)
  → LocalGrainFactory → generated GrainProxy (holds GrainId + IGrainCallInvoker)
  → proxy.MethodAsync() → LocalGrainCallInvoker.InvokeAsync()
  → GrainActivationTable.GetOrCreateAsync(grainId)
  → GrainActivation._queue (Channel<Func<Task>>) — serialises concurrent calls
  → IServiceScope created → behavior constructed from scope
  → behavior.MethodAsync()
  → scope disposed
```

## Remote (TCP gateway) call flow

```
TcpGatewayClusterClient.GetGrain<IMyGrain>(key)
  → TcpGatewayGrainFactory → generated GrainProxy (holds GrainId + TcpGatewayCallInvoker)
  → proxy.MethodAsync() → TcpGatewayCallInvoker.InvokeAsync()
  → GrainMessageSerializer.SerializeRequest()
  → TcpGatewayConnection (System.IO.Pipelines)
  → SiloMessagePump on the server → MessageDispatcher → LocalGrainCallInvoker
  → (same local flow from here)
  → response serialized back over the TCP pipe
```

## DI registration pattern

Grains, behaviors, proxies, and transport dispatchers use explicit registration. Nothing is discovered via assembly scanning (trim-unsafe). A typical silo registers:

```csharp
silo.Services.AddGrainBehavior<IMyGrain, MyBehavior>();
silo.Services.AddGrainTransportDispatcher(new GrainType("MyGrain"), new MyGrainProxy_TransportDispatcher());
silo.Services.AddScoped<IActivationMemory<MyState>>(sp =>
    new ActivationMemoryAccessor<MyState>(
        sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<MyState>()));
```

The `BehaviorRegistrationGenerator` eliminates this boilerplate — see [Source Generators](Source-Generators).

## Layered architecture

```
┌──────────────────────────────────────────┐
│  Programming model layer                 │
│  IGrain, IGrainBehavior, IActivationMemory│
├──────────────────────────────────────────┤
│  Runtime layer                           │
│  GrainActivation, GrainActivationTable   │
│  LocalGrainCallInvoker, PlacementDirector│
├──────────────────────────────────────────┤
│  Infrastructure layer                    │
│  IGrainStorage, IReminderService,        │
│  IStreamProvider, ITransport             │
├──────────────────────────────────────────┤
│  Tooling layer                           │
│  Roslyn generators, AOT analyzers        │
└──────────────────────────────────────────┘
```
