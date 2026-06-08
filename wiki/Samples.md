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
