---
name: quark-host-setup
description: Use when wiring a Quark silo or client host — UseQuark/UseQuarkClient, AddQuarkRuntime, TCP transport + gateway, localhost clustering, registering behaviors/proxies, storage/streams/codecs, placement strategies, grain timers, reminders, and transactions. Quark-specific host/Program.cs setup.
---

# Quark Host Setup

## Overview

A Quark process hosts a **silo** (`UseQuark`), a **client** (`UseQuarkClient`), or both in one host. Everything is **explicitly registered** — no assembly scanning (trim-unsafe). Wire it in `Program.cs` via `Host.CreateDefaultBuilder`.

## Silo + co-hosted client template

```csharp
IHost host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30001);     // gateway clients connect here

        // storage / streams / codecs
        silo.Services.AddInMemoryGrainStorage();
        silo.Services.AddMemoryStreams("myprovider");
        silo.Services.AddStreamableCodec<MyMsg, MyMsgCodec>();

        // grains — generated extension (preferred)
        silo.Services.AddMyGrainsBehaviors();
    })
    .UseQuarkClient(client =>
    {
        client.Services.AddLocalClusterClient();             // in-proc client to the co-hosted silo
        client.Services.AddMyGrainInterfacesGrainProxies();
    })
    .Build();

await host.StartAsync();
await host.WaitForShutdownAsync();
```

## Remote TCP gateway client (separate process)

```csharp
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30001);                   // or client.UseTcpGateway("host", port)
    client.AddTcpClientStreams("myprovider");            // optional silo→client stream push
    client.Services.AddStreamableCodec<MyMsg, MyMsgCodec>();
    client.Services.AddMyInterfacesGrainProxies();
})
// var grain = host.Services.GetRequiredService<IClusterClient>().GetGrain<IFoo>("key");
```

## Registration cheat-sheet

| Concern | Call (silo unless noted) |
|---|---|
| Runtime | `AddQuarkRuntime()` |
| Transport | `AddTcpTransport()` |
| Clustering / gateway | `UseLocalhostClustering(gatewayPort:)` |
| Behaviors (generated) | `Add{GrainsAssembly}Behaviors()` |
| Proxies (generated, client) | `Add{InterfacesAssembly}GrainProxies()` |
| Behavior (manual) | `AddGrainBehavior<IFoo, FooBehavior>()` + `AddGrainTransportDispatcher(new GrainType("FooGrain"), new FooGrainProxy_TransportDispatcher())` |
| Proxy (manual, client) | `AddGrainProxy<IFoo, FooGrainProxy>()` |
| Storage | `AddInMemoryGrainStorage([name])` / `AddRedisGrainStorage([name,] o => ...)` |
| Streams | `AddMemoryStreams("name")` + `AddStreamableCodec<T,TCodec>()` |
| Reminders | `AddInMemoryReminderService()` / `AddRedisReminderService(o => ...)` |
| Transactions | `UseTransactions()` + a storage provider |

Reference the projects: `Quark.Server` (silo meta), `Quark.Client` + `Quark.Client.Tcp` (client), `Quark.Serialization`, `Quark.Transport.Tcp`, `Quark.Streaming.InMemory`, `Quark.Persistence.InMemory`/`.Redis`, and `Quark.CodeGenerator` as an analyzer (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`). Do NOT add `Version=` to PackageReferences — versions are central in `Directory.Packages.props`.

## Placement (attribute on the behavior class)

```csharp
[HashBasedPlacement]   // deterministic silo by key hash — "sticky" + spreads across cluster
public sealed class MapBehavior : IGrainBehavior, IMapGrain { ... }
```

| Attribute | Behaviour |
|---|---|
| `[RandomPlacement]` (default) | any available silo |
| `[PreferLocalPlacement]` | prefer the calling silo |
| `[HashBasedPlacement]` | deterministic by key hash |
| `[LocalPlacement]` | must be local |
| `[StatelessWorker]` | multiple activations per silo |

## Timers (inside a behavior)

```csharp
// inject IActivationShellAccessor shell
S.Timer = shell.Shell.RegisterTimer<TState>(
    static async (state, _) => { /* runs on the grain's turn */ await Task.CompletedTask; },
    S,
    new GrainTimerCreationOptions { DueTime = TimeSpan.FromSeconds(1), Period = TimeSpan.FromSeconds(1) });
// dispose in OnDeactivateAsync (or call S.Timer.Dispose() to cancel early)
```

## Reminders (durable; implement `IRemindable`)

```csharp
await ctx.ReminderService.RegisterOrUpdateReminderAsync(ctx.GrainId, "name", dueTime, period);
public Task ReceiveReminder(string reminderName, TickStatus status) { ... }
```

## Transactions

```csharp
services.UseTransactions();
services.AddInMemoryGrainStorage("transactionStore");

public MyBehavior([TransactionalState("balance","transactionStore")] ITransactionalState<Bal> s) { ... }

[Transaction(TransactionOption.CreateOrJoin)]
public Task DepositAsync(decimal a) => _s.PerformUpdate(x => x.Balance += a);
```

## Common mistakes

- **Provider/name mismatches** across silo registration and `[FromKeyedServices]` / `GetStreamProvider`.
- **Forgetting `AddStreamableCodec`** on every host that carries the stream item over TCP.
- **`Version=` on PackageReferences** — breaks central package management.
- **Relying on assembly scanning** — Quark registers everything explicitly for AOT/trim safety.
- **Timer not disposed** on deactivation → leaks; dispose in `OnDeactivateAsync`.

## Related skills

- quark-writing-grains, quark-streaming, quark-persistence, quark-testing
