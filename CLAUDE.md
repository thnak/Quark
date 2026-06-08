# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Quark is a **Native AOT-first, Orleans-compatible distributed actor framework** for .NET 10. It follows the Orleans mental model (Grain, Silo, Client, Placement, Persistence) with three API compatibility tiers:
- **Drop-in** — same attribute/interface names and signatures as Orleans
- **Minor-change** — same concept, different DI wiring
- **Quark-native** — new concepts without direct Orleans equivalents

The runtime uses an **engine model** (M2): grain behaviors are POCOs implementing `IGrainBehavior`, constructed per-call inside a fresh `IServiceScope`. The old `Grain` base class is gone.

## Commands

```bash
# Build entire solution
dotnet build Quark.slnx

# Run all tests
dotnet test Quark.slnx

# Run a single test project
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj
dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj

# Run a single test by name
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GrainCallIntegrationTests"

# Native AOT smoke build (Linux)
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true
```

.NET SDK version is pinned to `10.0.201` in `global.json`. Package versions are managed centrally in `Directory.Packages.props`; do not add `Version=` attributes to individual `<PackageReference>` elements.

## Architecture

### Package layout

| Package | Role |
|---|---|
| `Quark.Core.Abstractions` | `GrainId`, `GrainType`, `IGrain`, `IGrainBehavior`, key-typed grain interfaces, `IActivationMemory<T>`, `IActivationLifecycle`, `IGrainFactory`, `IClusterClient`, `IGrainContext`, `ICallContext`, lifecycle, placement attributes |
| `Quark.Serialization.Abstractions` | `IFieldCodec<T>`, `IDeepCopier<T>`, `CodecWriter`/`CodecReader` (ZigZag+LEB128), `[GenerateSerializer]`/`[Id]`/`[Alias]` |
| `Quark.Transport.Abstractions` | `ITransport`, `ITransportListener`, `ITransportConnection` (IDuplexPipe), `MessageEnvelope` |
| `Quark.Core` | `ISiloBuilder`, `IClientBuilder`, `UseQuark()`/`UseQuarkClient()` host-builder extensions |
| `Quark.Runtime` | Silo-side runtime: `GrainActivation` (shell+mailbox), `GrainActivationTable`, `LocalGrainCallInvoker`, `SiloHostedService`, `GatewayMessagePump`, `GrainIdleCollector`, placement, clustering |
| `Quark.Client` | `LocalClusterClient`, `LocalGrainFactory`, proxy/observer factory registries |
| `Quark.Client.Tcp` | `TcpGatewayClusterClient`, `TcpGatewayGrainFactory`, TCP client stream push |
| `Quark.Serialization` | 18 primitive codecs, `CodecProvider`, `QuarkSerializer`, serialization DI |
| `Quark.Transport.Tcp` | `TcpTransport`/`TcpTransportListener`/`TcpTransportConnection` (System.IO.Pipelines), `TlsOptions` |
| `Quark.Persistence.Abstractions` | `IGrainStorage`, `IPersistentActivationMemory<T>`, `IPersistentState<T>`, `IStorage<T>`, `GrainState<T>`, `JournaledGrain<TState,TEvent>` |
| `Quark.Persistence.InMemory` | In-memory `IGrainStorage` + `ILogStorage` providers |
| `Quark.Persistence.Redis` | Redis-backed `IGrainStorage` provider (StackExchange.Redis) |
| `Quark.Reminders.Abstractions` | `IRemindable`, `IReminderService`, `IGrainReminder`, `DefaultReminderService` |
| `Quark.Reminders.InMemory` | In-process reminder store |
| `Quark.Reminders.Redis` | Redis-backed reminder store |
| `Quark.Streaming.Abstractions` | `IAsyncStream<T>`, `IAsyncObserver<T>`, `StreamId`, `StreamSequenceToken`, `[ImplicitStreamSubscription]` |
| `Quark.Streaming.InMemory` | In-memory stream provider |
| `Quark.Transactions` | `ITransactionalState<T>`, `[Transaction]`, `TransactionOption`, 2PC coordinator |
| `Quark.CodeGenerator` | Roslyn incremental generators: `GrainProxyGenerator`, `BehaviorRegistrationGenerator`, `SerializerGenerator` |
| `Quark.Analyzers` | AOT-safety Roslyn analyzers (QRK0001–QRK0003: dynamic type, Assembly.Load, ISerializable) |
| `Quark.Testing` | `TestCluster`/`TestSilo`/`TestClient` in-process test harness |

### Engine model — key concepts

| Term | Definition |
|---|---|
| **Shell** | `GrainActivation` — long-lived, owns the `Channel<Func<Task>>` mailbox. Holds `GrainId`, root `IServiceProvider`, and `StateHolder<TState>` bags. |
| **Behavior** | POCO class implementing `IGrainBehavior`. Resolved per call from a fresh `IServiceScope`, executes, then discarded. |
| **Activation memory** | `StateHolder<TState>` owned by the shell, exposed via `IActivationMemory<TState>`. Survives across calls; lost on deactivation. |
| **Persistent activation memory** | `IPersistentActivationMemory<TState>` — shell cache + `IGrainStorage` write-through. |

### Call flow for a grain method invocation

```
IClusterClient.GetGrain<IMyGrain>(key)
  → LocalGrainFactory → generated GrainProxy (holds GrainId + IGrainCallInvoker)
  → proxy.MethodAsync() → LocalGrainCallInvoker.InvokeAsync()
  → GrainActivationTable.GetOrCreateAsync(grainId)
  → GrainActivation._queue (Channel<Func<Task>>) — serialises concurrent calls
  → IServiceScope created from root IServiceProvider
  → behavior resolved from scope → behavior.MethodAsync()
  → scope disposed
```

For TCP remote calls, `TcpGatewayCallInvoker` serialises the request, sends it over the `TcpGatewayConnection`, and the silo's `SiloMessagePump` → `MessageDispatcher` routes it to the local invoker above.

### DI registration pattern

Everything is explicitly registered — no assembly scanning (trim-unsafe). A typical silo+client wiring:

```csharp
builder.UseQuark(silo =>
{
    silo.Services.AddQuarkRuntime();
    silo.Services.AddTcpTransport();
    silo.UseLocalhostClustering(gatewayPort: 30001);

    // Manual (pre-generator):
    silo.Services.AddGrainBehavior<IMyGrain, MyBehavior>();
    silo.Services.AddGrainTransportDispatcher(new GrainType("MyGrain"), new MyGrainProxy_TransportDispatcher());
    silo.Services.AddScoped<IActivationMemory<MyState>>(sp =>
        new ActivationMemoryAccessor<MyState>(
            sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<MyState>()));

    // Or with BehaviorRegistrationGenerator (preferred):
    silo.Services.AddMyAssemblyBehaviors();
});
builder.UseQuarkClient(client =>
{
    client.Services.AddLocalClusterClient();
    client.Services.AddGrainProxy<IMyGrain, MyGrainProxy>(); // emitted by GrainProxyGenerator
});
```

## Source generators

`Quark.CodeGenerator` ships three Roslyn incremental generators:

- **`GrainProxyGenerator`** — for every `interface` that inherits `IGrain`, emits `{InterfaceName[1..]}Proxy` routing calls through `IGrainCallInvoker`, plus a `{InterfaceName[1..]}Proxy_TransportDispatcher` for TCP serialization.
- **`BehaviorRegistrationGenerator`** — for every non-abstract class implementing `IGrainBehavior`, emits a single `QuarkRegistrations.g.cs` per assembly with `AddMyAssemblyBehaviors()` that registers all behaviors, transport dispatchers, and `IActivationMemory<T>` accessors. Diagnostics: `QRK0010` (no IGrain interface found), `QRK0011` (ambiguous interface).
- **`SerializerGenerator`** — for every type annotated `[GenerateSerializer]`, emits `IFieldCodec<T>` + `IDeepCopier<T>` using `[Id(uint)]`-tagged members.

In test projects, hand-write the invoker/proxy (see `tests/Quark.Tests.Unit/Integration/`) instead of relying on generators.

## Serialization conventions

- Apply `[GenerateSerializer]` to any type that crosses a TCP grain call boundary. In-process calls never serialize.
- Tag each serialized member with `[Id(uint)]` — IDs must be stable across versions; never reuse or renumber.
- `[Alias("name")]` provides a stable string alias for type-level versioning.
- `CodecWriter`/`CodecReader` use ZigZag + LEB128 variable-length encoding.
- For stream item types, also call `services.AddStreamableCodec<T, TCodec>()`.

## Placement strategies

Declare on the **behavior class**; `AttributePlacementStrategyResolver` picks the strategy at activation time:

| Attribute | Behaviour |
|---|---|
| `[RandomPlacement]` (default) | Activate on any available silo |
| `[PreferLocalPlacement]` | Prefer the silo handling the call |
| `[HashBasedPlacement]` | Deterministic silo via key hash |
| `[LocalPlacement]` | Must activate on the local silo |
| `[StatelessWorker]` | Multiple activations allowed per silo |

## Persistence

Four patterns, from ephemeral to fully event-sourced:

| Pattern | Interface | Use when |
|---|---|---|
| In-memory activation state | `IActivationMemory<T>` | State survives across calls but not deactivation |
| Persistent activation state | `IPersistentActivationMemory<T>` | Durable, explicit `WriteStateAsync()` |
| Named state injection | `[PersistentState("name","provider")] IPersistentState<T>` | Orleans-compatible named storage |
| Event sourcing | Inherit `JournaledGrain<TState,TEvent>` | Append-only event log, replay on activation |

Storage providers:
```csharp
services.AddInMemoryGrainStorage();                    // default provider
services.AddInMemoryGrainStorage("namedProvider");
services.AddRedisGrainStorage(opts => opts.ConnectionString = "...");
services.AddRedisGrainStorage("namedProvider", opts => { ... });
```

Idle deactivation is managed by `GrainIdleCollector`. Configure via `SiloRuntimeOptions.CollectionAge` / `CollectionInterval`. Call `_ctx.DelayDeactivation(TimeSpan)` to defer.

## Streaming

```csharp
// Server registration
silo.Services.AddMemoryStreams("providerName");
silo.Services.AddStreamableCodec<MyMsg, MyMsgCodec>();

// Get a stream and publish
var stream = provider.GetStream<MyMsg>(StreamId.Create("namespace", key));
await stream.OnNextAsync(msg);

// Subscribe
var handle = await stream.SubscribeAsync(observer);
await handle.UnsubscribeAsync();
```

TCP client stream push (`Quark.Client.Tcp`): add `client.AddTcpClientStreams("providerName")` on the client; the silo's `GatewayMessagePump` serializes and pushes messages over the open TCP connection.

`[ImplicitStreamSubscription("namespace")]` on a behavior auto-subscribes grains whose key matches the stream key.

## Reminders

```csharp
// Register provider
services.AddInMemoryReminderService();
services.AddRedisReminderService(opts => opts.ConnectionString = "...");

// In behavior (implement IRemindable)
await _ctx.ReminderService.RegisterOrUpdateReminderAsync(_ctx.GrainId, "name", dueTime, period);
await _ctx.ReminderService.UnregisterReminderAsync(_ctx.GrainId, "name");
public Task ReceiveReminder(string reminderName, TickStatus status) { ... }
```

## Timers

```csharp
IGrainTimer timer = _ctx.RegisterGrainTimer<TState>(
    callback,
    state,
    new GrainTimerCreationOptions { DueTime = ..., Period = ..., Interleave = false });
// Automatically disposed on deactivation; call timer.Dispose() to cancel early.
```

## Transactions

```csharp
services.UseTransactions();
services.AddInMemoryGrainStorage("transactionStore"); // or Redis

// In behavior
public MyBehavior([TransactionalState("balance","transactionStore")] ITransactionalState<BalanceState> state)
{ ... }

[Transaction(TransactionOption.CreateOrJoin)]
public Task DepositAsync(decimal amount) => _state.PerformUpdate(s => s.Balance += amount);
```

## TCP gateway client

```csharp
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30001);       // or client.UseTcpGateway("host", port)
    client.AddTcpClientStreams("chat");       // optional stream push
    client.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
    client.Services.AddGrainProxy<IMyGrain, MyGrainProxy>();
})
```

## AOT / trim constraints

Every production package has `IsTrimmable=true` and `EnableAotAnalyzer=true` (set in `Directory.Build.props`). New code must:

1. Prefer source generation over runtime reflection.
2. Annotate unavoidable dynamic calls with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.
3. Guard JIT-only paths with `RuntimeFeature.IsDynamicCodeSupported`.
4. Use `[UnsafeAccessor]` instead of `DynamicMethod` for private-member access.
5. Never introduce `ISerializable`-based patterns (triggers QRK0003).
6. Use explicit provider registration; avoid assembly-scanning discovery.

## Testing

Use `Quark.Testing.Harness.TestCluster` for in-process integration tests:

```csharp
await using var cluster = await TestCluster.CreateAsync(options =>
{
    options.ConfigureSiloServices = services =>
    {
        services.AddGrainBehavior<IMyGrain, MyBehavior>();
        services.AddScoped<IActivationMemory<MyState>>(sp =>
            new ActivationMemoryAccessor<MyState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<MyState>()));
    };
    options.ConfigureClientServices = services =>
        services.AddGrainProxy<IMyGrain, MyGrainProxy>();
});
var grain = cluster.Client.GetGrain<IMyGrain>("key");
```

Tests requiring Redis use Testcontainers (`Testcontainers` package, `Quark.Tests.Integration`). Skip integration tests when infrastructure is unavailable via `[Trait("category","integration")]`.

In test projects, hand-write invokers and proxies rather than running the code generators — simpler and avoids circular project references.
