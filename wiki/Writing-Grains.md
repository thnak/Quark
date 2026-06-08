# Writing Grains

A grain in Quark consists of three things:

1. A **grain interface** — the public contract (`IGrainWith*Key`)
2. A **grain behavior** — a POCO class implementing `IGrainBehavior` and the interface
3. **Registration** — wiring up the behavior, proxy, and activation memory in DI

## Grain interfaces

Extend one of the key-typed marker interfaces:

```csharp
// String key (most common)
public interface IPlayerGrain : IGrainWithStringKey
{
    Task SetInfoAsync(string name);
    Task<string> DescribeAsync();
}

// Integer key
public interface IRoomGrain : IGrainWithIntegerKey
{
    Task<RoomInfo?> GetInfoAsync();
}

// Guid key
public interface ISessionGrain : IGrainWithGuidKey
{
    Task<bool> IsActiveAsync();
}

// Compound keys
public interface IReportGrain : IGrainWithIntegerCompoundKey
{
    Task GenerateAsync();
}
```

All interface methods must return `Task` or `Task<T>`.

## Grain behaviors

A behavior is a plain class that:
- Implements `IGrainBehavior` (marker interface)
- Implements the grain interface(s)
- Receives all dependencies through its constructor

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

    private PlayerState S => _memory.Value;

    public Task SetInfoAsync(string name)
    {
        S.Name = name;
        return Task.CompletedTask;
    }

    public Task<string> DescribeAsync()
        => Task.FromResult(S.Name ?? "unnamed");
}
```

A new `PlayerBehavior` instance is created for every method call and disposed when the call returns.

## Grain state

State that must survive across calls lives in `IActivationMemory<TState>`:

```csharp
public sealed class PlayerState   // plain class, new() constraint
{
    public string? Name { get; set; }
    public IRoomGrain? Room { get; set; }
    public List<Thing> Inventory { get; } = [];
}
```

The state object is owned by the grain's shell (`GrainActivation`) and injected into the behavior via `IActivationMemory<TState>`. It is not persisted — it is lost when the activation is deactivated or the silo restarts. For durable state, see [Persistence](Persistence).

## Call context

`ICallContext` (injected like any other service) gives the behavior access to the current grain identity:

```csharp
_ctx.GrainId           // GrainId (key + type)
_ctx.GrainId.Key       // raw key string
```

Use `ICallContext` to obtain your own `GrainId` when constructing a self-reference:

```csharp
var self = _factory.GetGrain<IMyGrain>(_ctx.GrainId);
```

## Lifecycle hooks

Implement `IActivationLifecycle` to run code on activation and deactivation:

```csharp
public sealed class ChannelBehavior : IGrainBehavior, IChannelGrain, IActivationLifecycle
{
    public Task OnActivateAsync(CancellationToken ct)
    {
        // Runs once when the grain is first activated
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        // Runs before the grain is deactivated
        return Task.CompletedTask;
    }
}
```

`OnActivateAsync` is called once before any method call on a fresh activation. `OnDeactivateAsync` is called during idle-timeout collection or silo shutdown.

## Reentrancy

By default, a grain processes one message at a time. Mark the behavior with `[Reentrant]` to allow concurrent dispatch (useful for read-heavy grains):

```csharp
[Reentrant]
public sealed class CatalogBehavior : IGrainBehavior, ICatalogGrain
{
    // Multiple calls may be in-flight simultaneously
}
```

## Accessing other grains

Inject `IGrainFactory` and call `GetGrain<T>`:

```csharp
var room = _factory.GetGrain<IRoomGrain>(1L);
await room.Enter(playerRef);
```

## Grain observers

An observer is a client-side object that receives grain push notifications. Declare an observer interface extending `IGrainObserver`:

```csharp
public interface IGameObserver : IGrainObserver
{
    void OnEvent(GameEvent ev);
}
```

On the client, implement the interface and wrap it with `CreateObjectReference`:

```csharp
var observer = new MyGameObserver();
var observerRef = factory.CreateObjectReference<IGameObserver>(observer);
await grain.Subscribe(observerRef);
```

Call `DeleteObjectReference` when done.

## Placement strategies

Apply placement attributes to the **behavior class**:

```csharp
[RandomPlacement]    // default — activate on any available silo
[PreferLocalPlacement]
[HashBasedPlacement]
[LocalPlacement]
[StatelessWorker]    // multiple activations per silo
```

## Primary key helpers

The `GrainExtensions` static class provides Orleans-compatible helpers:

```csharp
grain.GetPrimaryKeyString()  // IGrainWithStringKey
grain.GetPrimaryKey()        // IGrainWithGuidKey → Guid
grain.GetPrimaryKeyLong()    // IGrainWithIntegerKey → long
```

Inside a behavior, read the key directly from `ICallContext.GrainId.Key`.

## Self-reference

Use `AsReference<T>()` (from `GrainExtensions`) to get a reference to yourself that can be passed to other grains:

```csharp
var self = this.AsReference<IPlayerGrain>(_factory, _ctx.GrainId);
await room.Enter(self);
```

## Registering a grain

```csharp
// In silo startup:
silo.Services.AddGrainBehavior<IMyGrain, MyBehavior>();
silo.Services.AddGrainTransportDispatcher(
    new GrainType("MyGrain"),
    new MyGrainProxy_TransportDispatcher()); // emitted by BehaviorRegistrationGenerator

silo.Services.AddScoped<IActivationMemory<MyState>>(sp =>
    new ActivationMemoryAccessor<MyState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<MyState>()));

// In client startup:
client.Services.AddGrainProxy<IMyGrain, MyGrainProxy>(); // emitted by GrainProxyGenerator
```

The `BehaviorRegistrationGenerator` source generator emits a single `AddMyAssemblyBehaviors()` extension method that replaces all of the above boilerplate — see [Source Generators](Source-Generators).
