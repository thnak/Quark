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

### The one rule: behavior fields are per-call

This is the habit that changes coming from Orleans (`Grain` subclasses) or Akka.NET (actor classes): **a mutable field on a behavior does not survive to the next call.** The engine rebuilds the behavior from a fresh `IServiceScope` on every invocation, so fields are scratch space for a single call.

Akka.NET's actor-object model — where a user actor instance is long-lived and its fields *are* the
actor's state — is not wrong; it is a coherent design that Quark deliberately does not follow. In
Quark the long-lived runtime object is the `GrainActivation` shell, not the behavior object: the
behavior is execution logic the shell runs per call, and the shell is what owns identity, ordering,
state lifetime, timers, disposal, and placement. Anything a behavior needs to remember between
calls has to be handed to the shell through one of the APIs below, because the behavior instance
itself is discarded the moment the call returns.

```csharp
public sealed class WrongCounter : IGrainBehavior, ICounterGrain
{
    private int _count;                                  // ⚠ QRK0020: reset between calls

    public Task IncrementAsync() { _count++; return Task.CompletedTask; }
    public Task<int> GetAsync() => Task.FromResult(_count); // always racing back to 0/1
}
```

`_count` belongs to one `WrongCounter` instance, which lives for exactly one call. The next
`IncrementAsync` call gets a brand-new instance with `_count` back at zero — the field looks like
state but isn't. The corrected shape routes the same counter through the shell via
`IActivationMemory<T>`, so it survives across calls on the activation:

```csharp
public sealed class RightCounter : IGrainBehavior, ICounterGrain
{
    private readonly IActivationMemory<CounterState> _memory;
    public RightCounter(IActivationMemory<CounterState> memory) => _memory = memory;

    public Task IncrementAsync() { _memory.Value.Count++; return Task.CompletedTask; }
    public Task<int> GetAsync() => Task.FromResult(_memory.Value.Count); // survives across calls
}
```

You do not have to remember this — the build does:

- `QRK0020` warns on any mutable instance field of a behavior; `QRK0021` on writable auto-properties.
- `readonly` constructor-injected fields (services, `IActivationMemory<T>` handles, `ICallContext`) are the intended pattern and produce no warning. Injected dependencies and temporary per-call helpers are fine as fields precisely because nothing expects them to survive the call — only *state* is the problem.
- `BehaviorStartupValidator` constructs every registered behavior at silo startup, so a missing DI registration fails at boot rather than on the first live call.
- `QRK0022` warns on mutable `static` fields/properties on a behavior — including `static readonly` fields of mutable collection types (e.g. `Dictionary<,>`), since readonly-ness of the reference doesn't make the contents immutable.

Mutable static state is not grain state — it is process-wide state that happens to be reachable
from a behavior class, and it fails in two distinct ways:

- **Cross-grain leakage within one process.** A silo hosts many activations of the same grain type side by side; a mutable static field is shared by all of them, so one grain's write is visible to every other grain's read. It is also not thread-safe under `[Reentrant]`/`[StatelessWorker]`, where multiple calls can run concurrently against static state with no synchronization.
- **Inconsistency across silos.** Static state lives in one silo's process memory only. A cluster with more than one silo has one copy per silo, each silo sees a different value, and none of it is visible to placement, persistence, or diagnostics — the engine has no way to migrate, replicate, or even know about it.

`static readonly` **caches or configuration** — a compiled regex, a loaded config object, a lookup
table built once at startup — are fine as static state precisely because they hold no grain, user,
or application state; they are the same value on every silo and never mutated after initialization.
The distinction QRK0022 draws is mutability, not "static" itself.

Where cross-call state actually lives, from cheapest to most durable: `IActivationMemory<T>` (survives calls, lost on deactivation) → `IManagedActivationMemory<T>` (adds async init/cleanup for resources) → `IPersistentActivationMemory<T>` / `[PersistentState]` (durable) → `JournaledGrain` (event-sourced). Background work and any resource a behavior needs (a connection, a buffer, a subscription) must be owned by one of these lifecycle APIs — or by timers/reminders — rather than kept alive by the behavior instance, which has no `Dispose` the engine will call for you. The full decision table with lifecycle diagrams is in [Persistence](Persistence); the engine's lifetime and failure contract is in [Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics).

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

## Opt-in user-service-provider factory

By default, a behavior's constructor dependencies are re-resolved from a fresh `IServiceScope` on
**every** grain call. That's the right default, but it's wasteful when a behavior's own (non-Quark)
dependencies form an expensive graph (a repository backed by a connection pool, a rules engine, ...)
that's effectively stateless/reusable across calls. Implement `IGrainUserServiceProviderFactory`
directly on the behavior class to supply a cached `IServiceProvider`, built once per grain **type** at
silo startup, that the behavior's own constructor parameters are resolved from on every subsequent
call:

```csharp
public sealed class OrderRepository
{
    public OrderRepository(/* expensive connection pool, rules engine, etc. */) { }
}

public sealed class OrderBehavior : IGrainBehavior, IOrderGrain, IGrainUserServiceProviderFactory
{
    private readonly OrderRepository _repo;
    private readonly IActivationMemory<OrderState> _memory;

    public OrderBehavior(OrderRepository repo, IActivationMemory<OrderState> memory)
    {
        _repo = repo;
        _memory = memory;
    }

    // Called once per grain type at silo startup; the returned provider is cached and shared by
    // every activation of IOrderGrain for the process lifetime.
    public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices) => rootServices;

    public Task<string> GetTotalAsync() => Task.FromResult(_repo.GetTotal(_memory.Value));
}
```

```csharp
// In silo startup:
silo.Services.AddSingleton<OrderRepository>();
silo.Services.AddMyAssemblyBehaviors(); // generator detects the opt-in and wires up the factory
```

`CreateUserServiceProvider` runs once per grain type — at silo startup, not per activation, not per
call — and a throwing implementation fails silo startup before any activation is attempted. Quark's own
per-call services (`ICallContext`, `IActivationMemory<T>`, etc.) are always resolved from Quark's own
scope, never from the cached user provider, even if the developer's provider happens to also contain a
registration for one of those types. `IPersistentActivationMemory<T>`/`[PersistentState]` are not yet
supported on opted-in behaviors (v1 limitation — see [Source Generators](Source-Generators)). See
[Architecture](Architecture#opt-in-user-service-provider-factory-per-grain-type-cached-di) for the full
binder pipeline.

## Data access from behaviors

Per-call scoping means a scoped `DbContext` (or any unit-of-work service) is injected into your
behavior exactly like in an ASP.NET Core request — fresh per call, disposed with the call, no
manual scope juggling. Be clear about what that buys and what it doesn't:

**What the per-call scope solves:** lifetime correctness. No stale change trackers, no `DbContext`
shared across concurrent calls, natural per-tenant wiring via a fresh per-call DI scope.

**What it does not solve:** database performance. A scope per call is not a connection per call —
but it isn't free batching either. The guidance:

- **Let the driver pool connections.** ADO.NET/`SqlClient`, Npgsql, and Redis multiplexers pool at
  the process level; a short-lived `DbContext` borrows and returns a pooled connection. Register
  clients that are *designed* to be long-lived (e.g. `IConnectionMultiplexer`, `HttpClient` via
  factory) as singletons — per-call scoping is for units of work, not for expensive clients.
- **Batch inside the call, not across calls.** Do one `SaveChanges`/pipeline per grain call, not one
  per mutation. The mailbox already serializes all writers for a grain key, so a grain call is a
  natural write batch with no extra locking.
- **Use the grain as the write owner.** Route all writes for an entity through its grain instead of
  letting N services open N contexts against the same row — that is the actor model doing your
  contention control.
- **Cache reads in activation memory.** If every call re-reads the same row, load it once into
  `IPersistentActivationMemory<T>` (shell-cached, explicit `WriteStateAsync`) instead of using a
  `[PersistentState]` slot, which re-reads storage on every call — see the comparison in
  [Persistence](Persistence).
- **Keep grain calls idempotent where callers may retry.** Delivery-guarantee formalization and
  idempotency-key support are tracked in [#59](https://github.com/thnak/Quark/issues/59) and
  [#124](https://github.com/thnak/Quark/issues/124); until then, retry policy is yours.
- **For accumulate-and-flush workloads**, buffer in `IManagedActivationMemory<T>` and flush on a
  grain timer and in `Destroy` — the deactivation contract guarantees the flush runs
  ([Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics#what-deactivation-guarantees)).

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

A non-reentrant behavior that `await`s a call back into its own grain interface risks a classic
single-mailbox deadlock: if the call target is this same activation, its mailbox is already
occupied processing the current call, so the callback can never be scheduled. `QRK0040` flags the
common inline shape of this (`await _self.MethodAsync()` where the behavior implements the
interface `_self` is typed as) at compile time — see
[Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics#mailbox-ordering-and-backpressure) for the
full deadlock-surface writeup and the heuristic's known false-positive/false-negative shape.

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
