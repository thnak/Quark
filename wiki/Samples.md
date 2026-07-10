# Samples

## Adventure

A two-process text adventure game demonstrating grain-to-grain calls, grain timers, `IActivationMemory<T>`, and the TCP gateway client.

**Source:** `samples/Adventure/`

### Architecture

```
Adventure.GrainInterfaces   — IPlayerGrain, IRoomGrain, IMonsterGrain (shared)
Adventure.Grains            — PlayerBehavior, RoomBehavior, MonsterBehavior
Adventure.Server            — silo host + map loader
Adventure.Client            — TCP gateway client + interactive loop
```

### Running

```bash
# Terminal 1 — Server
dotnet run --project samples/Adventure/Adventure.Server

# Terminal 2 — Client
dotnet run --project samples/Adventure/Adventure.Client
```

Commands: `look`, `go north/south/east/west`, `take <item>`, `drop <item>`, `inv`, `kill <monster>`, `quit`.

### Key patterns

**Behavior with activation memory:**

```csharp
public sealed class PlayerBehavior : IGrainBehavior, IPlayerGrain
{
    private readonly IActivationMemory<PlayerState> _memory;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;

    public PlayerBehavior(
        IActivationMemory<PlayerState> memory,
        IGrainFactory factory,
        ICallContext ctx)
    {
        _memory = memory;
        _factory = factory;
        _ctx = ctx;
    }

    public async Task<string> GoAsync(string direction)
    {
        var info = await _memory.Value.Room!.GetInfoAsync();
        // ... navigate
    }
}
```

**Server registration (manual, pre-BehaviorRegistrationGenerator):**

```csharp
silo.Services.AddGrainBehavior<IPlayerGrain, PlayerBehavior>();
silo.Services.AddGrainTransportDispatcher(
    new GrainType("PlayerGrain"),
    new PlayerGrainProxy_TransportDispatcher());
silo.Services.AddScoped<IActivationMemory<PlayerState>>(sp =>
    new ActivationMemoryAccessor<PlayerState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<PlayerState>()));
```

**TCP client:**

```csharp
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30001);
    client.Services.AddGrainProxy<IPlayerGrain, PlayerGrainProxy>();
})
```

**Grain timer on MonsterBehavior:**

```csharp
public Task OnActivateAsync(CancellationToken ct)
{
    _patrolTimer = _ctx.RegisterGrainTimer<object?>(
        static (_, _) => ProwlAsync(),
        null,
        new GrainTimerCreationOptions { DueTime = TimeSpan.FromSeconds(2), Period = TimeSpan.FromSeconds(2) });
    return Task.CompletedTask;
}
```

---

## ChatRoom

A real-time chat demo demonstrating in-memory streams, TCP client stream subscriptions, and the `[FromKeyedServices]` DI pattern for named stream providers.

**Source:** `samples/ChatRoom/`

### Architecture

```
ChatRoom.Common   — IChannelGrain, ChatMsg (shared)
ChatRoom.Server   — ChannelBehavior, silo host
ChatRoom.Client   — TCP gateway client + Spectre.Console UI
```

### Running

```bash
# Terminal 1 — Server
dotnet run --project samples/ChatRoom/ChatRoom.Server

# Terminal 2+ — Clients
dotnet run --project samples/ChatRoom/ChatRoom.Client
```

Client commands: `/j <channel>` join, `/l` leave, `/h` history, `/m` members, `/n <name>` rename, `/exit`.

### Key patterns

**Stream-enabled behavior using `[FromKeyedServices]`:**

```csharp
public sealed class ChannelBehavior : IGrainBehavior, IChannelGrain, IActivationLifecycle
{
    private readonly IActivationMemory<ChannelState> _memory;
    private readonly ICallContext _ctx;
    private readonly IStreamProvider? _chatProvider;

    public ChannelBehavior(
        IActivationMemory<ChannelState> memory,
        ICallContext ctx,
        [FromKeyedServices("chat")] IStreamProvider? chatProvider = null)
    { ... }

    public Task OnActivateAsync(CancellationToken ct)
    {
        S.StreamId = StreamId.Create("ChatRoom", _ctx.GrainId.Key);
        S.Stream = _chatProvider!.GetStream<ChatMsg>(S.StreamId);
        return Task.CompletedTask;
    }

    public async Task<bool> Message(ChatMsg msg)
    {
        S.History.Add(msg);
        await S.Stream!.OnNextAsync(msg);
        return true;
    }
}
```

**Server registration (stream provider + codec):**

```csharp
silo.Services.AddMemoryStreams("chat");
silo.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
```

**Client subscribing to TCP-pushed stream:**

```csharp
var grain = clusterClient.GetGrain<IChannelGrain>("general");
var streamId = await grain.Join(username);

var stream = streamProvider.GetStream<ChatMsg>(streamId);
var handle = await stream.SubscribeAsync(new StreamObserver("general"));
```

When the server grain calls `stream.OnNextAsync(msg)`, the `GatewayMessagePump` serializes the message and pushes it over the client's TCP connection. The client receives it in `StreamObserver.OnNextAsync` without polling.

### Spectre.Console UI

The client uses [Spectre.Console](https://spectreconsole.net/) for colored terminal output. It is not a Quark dependency — replace with any console library.

## Persistence ("Bank")

A two-process silo + TCP client that puts five persistence / activation-memory patterns side by side, all keyed by the same account id. The best end-to-end reference for [Persistence](Persistence).

**Source:** `samples/Persistence/`

### Architecture

```
Bank.GrainInterfaces   — IAccountGrain, IProfileGrain, ILedgerGrain, IVaultGrain, IStatementGrain
Bank.Grains            — five behaviors + state/events + eager/managed resources
Bank.Server            — silo host: storage providers + generated registrations
Bank.Client            — TCP gateway client + interactive loop
```

### Running

```bash
# Terminal 1 — Server (gateway on port 30005)
dotnet run --project samples/Persistence/Bank.Server

# Terminal 2 — Client
dotnet run --project samples/Persistence/Bank.Client
```

Commands: `deposit/withdraw/balance` (activation memory), `profile/whoami` (named state), `credit/debit/ledger/history` (event sourcing), `vault/accrue/rate` (eager memory), `note/statement/close` (managed memory), `help`, `quit`.

### Patterns demonstrated

| Grain | Pattern | Mechanism |
|---|---|---|
| `IAccountGrain` | Persistent activation memory | `IPersistentActivationMemory<AccountState>` — shell-cached, write-through on `SaveAsync` |
| `IProfileGrain` | Named persistent state | `[PersistentState("profile")] IPersistentState<ProfileState>` — per-call storage slot |
| `ILedgerGrain` | Event sourcing | `JournaledGrain<LedgerState, LedgerEvent>` — append-only log replayed on activation |
| `IVaultGrain` | Eager activation memory | `IEagerActivationMemory<RateSnapshot>` — DI-aware factory runs before `OnActivateAsync`, value read synchronously |
| `IStatementGrain` | Managed activation memory | `IManagedActivationMemory<StatementBuffer>` — lazy async init, `Destroy` flush on deactivation |

**Storage wiring (server):**

```csharp
silo.Services.AddInMemoryGrainStorage();                       // default IGrainStorage
silo.Services.AddSingleton<ILogStorage, InMemoryLogStorage>(); // event log
silo.Services.AddBankStateCopiers();                           // IDeepCopier<T> for stored state
silo.Services.AddBankGrainsBehaviors();                        // generated registration
```

State written through `IGrainStorage` carries `[GenerateSerializer]` so the generator can emit the deep copier the provider uses to snapshot it. The event-sourced `LedgerState` needs no serializer — it is rebuilt from events. Swap `AddInMemoryGrainStorage()` for `AddRedisGrainStorage(...)` to persist across restarts with no grain-code changes. See the sample README for a full walkthrough.

## Realm

An Intersect-inspired MMO spatial backbone: maps, tile grids, scene transitions, movement authority, and Area-of-Interest (AoI) broadcast, plus a TCP bot-driver load harness. The most complete demonstration of placement, streaming, and timers working together at scale.

**Source:** `samples/Realm/`

### Architecture

| Grain | Cardinality | Placement | Responsibility |
|---|---|---|---|
| `WorldGrain` | 1 (singleton) | `[HashBasedPlacement]` fixed key | World directory: map registry, spawn lookup, player login → assigns starting map. |
| `MapGrain` | 1 per map | `[HashBasedPlacement]` | Authoritative tile grid, live entity roster (players + NPCs), per-tick simulation, the map's AoI broadcast stream. Sticky to one silo. |
| `PlayerGrain` | 1 per player | `[PreferLocalPlacement]` | Per-player session: current map + coords, persistent state, move-intent ingress, AoI stream subscriptions (current map + cardinal neighbors). |

NPCs are in-grain structs inside `MapGrain`, not separate grains — cheaper than one grain per NPC and Intersect-faithful. Each map runs a simple wander AI each tick, subject to the same bounds/collision rules as players.

```
samples/Realm/
Realm.Common/          — DTOs, content models, stream-namespace constants
Realm.GrainInterfaces/ — IWorldGrain, IMapGrain, IPlayerGrain
Realm.Grains/          — WorldBehavior, MapBehavior, PlayerBehavior
Realm.Content/         — static JSON world + content loader
Realm.Server/          — silo host: TCP gateway, streams, storage, diagnostics
Realm.Client/          — batch bot-driver load harness
```

### Running

```bash
# Terminal 1 — server (TCP gateway on port 30010)
dotnet run --project samples/Realm/Realm.Server

# Terminal 2 — bot-driver load harness
dotnet run --project samples/Realm/Realm.Client -- --players 20 --rate 2 --duration 15
```

### Key patterns

**AoI broadcast via per-map streams:**

```csharp
[ImplicitStreamSubscription(RealmConstants.StreamProvider)]
// MapGrain publishes a batched DeltaBatch each tick; PlayerGrain subscribes to its
// current map plus cardinal (N/S/E/W) neighbor maps, and re-subscribes on border crossing.
```

**Diagnostics wired for hang detection:**

```csharp
silo.Services.AddQuarkDiagnostics<RealmDiagnosticsListener>();
silo.Services.AddQuarkStuckGrainDetector();
```

See the sample's `README.md` and `ROADMAP.md` for the full architecture diagram, benchmark results, and a log of real framework gaps found while building this sample (a scheduler reentrancy deadlock, a `Quark.Diagnostics` circular-DI bug, and a multi-silo activation-placement gap).
