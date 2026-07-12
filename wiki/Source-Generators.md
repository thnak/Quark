# Source Generators

`Quark.CodeGenerator` ships four Roslyn incremental generators. All generated code is AOT-safe — no reflection, no `MakeGenericType`, no runtime type lookups on hot paths.

## What each generator emits

Two inputs you write — a grain **interface** and a **behavior** class — plus `[GenerateSerializer]`
**state/DTO** types drive four generators. Each input fans out to the artifacts the client and
silo consume at runtime.

```mermaid
flowchart TB
    subgraph You["Your code"]
        IFACE["interface IMyGrain : IGrain"]
        BEH["class MyBehavior :<br/>IGrainBehavior, IMyGrain"]
        DTO["[GenerateSerializer]<br/>MyState"]
    end
    subgraph Gen["Quark.CodeGenerator"]
        GPG[GrainProxyGenerator]
        CPRG[ClientProxyRegistrationGenerator]
        BRG[BehaviorRegistrationGenerator]
        SG[SerializerGenerator]
    end
    IFACE --> GPG
    IFACE --> CPRG
    BEH --> BRG
    DTO --> SG

    GPG --> PROXY["MyGrainProxy<br/>(client-side, implements IMyGrain)"]
    GPG --> DISP["MyGrainProxy_TransportDispatcher<br/>(silo-side, .Instance)"]
    CPRG --> CREG["AddMyAssemblyGrainProxies()<br/>registers every IGrain/IGrainObserver proxy in the assembly"]
    BRG --> REG["AddMyAssemblyBehaviors()<br/>registers behavior + dispatcher + memory accessors"]
    SG --> CODEC["MyStateCodec : IFieldCodec&lt;T&gt;"]
    SG --> COPIER["MyStateCopier : IDeepCopier&lt;T&gt;"]

    REG -. registers .-> DISP
    CREG -. registers .-> PROXY
```

## Grain ↔ proxy relationship at runtime

The **proxy** (client) and the **transport dispatcher** (silo) are two halves of the same call. The
proxy implements the grain interface and forwards each method as an *invokable* through an
`IGrainCallInvoker`; over TCP the dispatcher reverses the serialization and re-invokes the real
behavior locally.

```mermaid
flowchart LR
    CALL["caller holds IMyGrain<br/>(actually MyGrainProxy)"]
    CALL -->|"MethodAsync()"| PROXY[MyGrainProxy]
    PROXY -->|"InvokeAsync(grainId, invokable)"| INV[IGrainCallInvoker]

    INV -->|in-process| LOCAL[LocalGrainCallInvoker]
    INV -->|remote| TCP[TcpGatewayCallInvoker]
    TCP -. "serialized over TCP" .-> DISP["MyGrainProxy_TransportDispatcher<br/>(on the silo)"]
    DISP --> LOCAL
    LOCAL --> BEH["MyBehavior.MethodAsync()<br/>(the real grain code)"]
```

Both invokers reach the same `MyBehavior` — the proxy hides whether the grain is local or across the
network. The pairing is by **grain type**: `AddGrainProxy<IMyGrain, MyGrainProxy>()` on the client and
`AddGrainTransportDispatcher(new GrainType("MyGrain"), MyGrainProxy_TransportDispatcher.Instance)` on
the silo. In generator-driven projects neither call is hand-written — `ClientProxyRegistrationGenerator`
emits the former into `AddMyAssemblyGrainProxies()` and `BehaviorRegistrationGenerator` emits the
latter into `AddMyAssemblyBehaviors()`.

## GrainProxyGenerator

For every `interface` that inherits `IGrain`, emits a `{InterfaceName[1..]}Proxy` class that:

- Implements the interface
- Holds a `GrainId` and an `IGrainCallInvoker`
- Routes each method call through `IGrainCallInvoker.InvokeAsync`
- Emits a companion `{InterfaceName[1..]}Proxy_TransportDispatcher` for TCP serialization/deserialization

### Input

```csharp
public interface ICounterGrain : IGrainWithStringKey
{
    Task IncrementAsync();
    Task<int> GetAsync();
}
```

### Generated output (simplified)

```csharp
// CounterGrainProxy.g.cs
public sealed class CounterGrainProxy : ICounterGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public CounterGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task IncrementAsync()
        => _invoker.InvokeAsync(_grainId, new IncrementInvokable());

    public Task<int> GetAsync()
        => _invoker.InvokeAsync<int>(_grainId, new GetInvokable());
}

// CounterGrainProxy_TransportDispatcher.g.cs
public sealed class CounterGrainProxy_TransportDispatcher : IGrainTransportDispatcher
{
    public static readonly CounterGrainProxy_TransportDispatcher Instance = new();
    // Handles serialize/deserialize for each method over TCP
}
```

### Usage

The proxy can be registered on the client side one interface at a time:

```csharp
client.Services.AddGrainProxy<ICounterGrain, CounterGrainProxy>();
```

The transport dispatcher is registered on the silo side the same way (or, more commonly, via
`BehaviorRegistrationGenerator` — see below):

```csharp
silo.Services.AddGrainTransportDispatcher(
    new GrainType("CounterGrain"),
    CounterGrainProxy_TransportDispatcher.Instance);
```

In practice, prefer letting `ClientProxyRegistrationGenerator` (next section) register every proxy
in the assembly at once rather than calling `AddGrainProxy<,>()` per interface.

## ClientProxyRegistrationGenerator

For every `interface` in the assembly that derives from `IGrain` or `IGrainObserver`, emits one
`Add{AssemblyName}GrainProxies()` extension method (in a shared `QuarkClientRegistrations` partial
class) that registers all of their generated proxies on the client in a single call:

- `IGrain`-derived interfaces → `AddGrainProxy<TInterface, TProxy>()`
- `IGrainObserver`-derived interfaces → `AddObserverProxy<TInterface, TProxy>()`

The proxy type name is derived the same way `GrainProxyGenerator` names its output (`ICounterGrain`
→ `CounterGrainProxy`), so the two generators always agree on what to reference.

### Input

Any `IGrain`/`IGrainObserver` interfaces already in the assembly — no extra attribute needed:

```csharp
public interface ICounterGrain : IGrainWithStringKey
{
    Task IncrementAsync();
    Task<int> GetAsync();
}

public interface IGameObserver : IGrainObserver
{
    void OnEvent(GameEvent ev);
}
```

### Generated output (simplified)

```csharp
// QuarkClientRegistrations.g.cs
public static partial class QuarkClientRegistrations
{
    public static IServiceCollection AddMyGrainInterfacesGrainProxies(this IServiceCollection services)
    {
        Quark.Client.ClientServiceCollectionExtensions.AddGrainProxy<ICounterGrain, CounterGrainProxy>(services);
        Quark.Client.ClientServiceCollectionExtensions.AddObserverProxy<IGameObserver, GameObserverProxy>(services);
        return services;
    }
}
```

The method name is derived from the assembly name (`MyGrainInterfaces` → `AddMyGrainInterfacesGrainProxies`),
mirroring how `BehaviorRegistrationGenerator` names `AddMyAssemblyBehaviors()` from the *silo-side*
assembly.

### Usage

```csharp
// Replaces per-interface AddGrainProxy<,>()/AddObserverProxy<,>() calls:
.UseQuarkClient(client =>
{
    client.Services.AddLocalClusterClient();     // or UseLocalhostGateway/UseTcpGateway for TCP
    client.Services.AddMyGrainInterfacesGrainProxies();
})
```

This is the pattern used by current samples (`ChatRoom`, `Persistence`/Bank, `Realm`) — one generated
call registers every grain and observer proxy declared in the interfaces assembly, instead of one
`AddGrainProxy<,>()`/`AddObserverProxy<,>()` line per interface.

### Trigger conditions

- Runs over every `interface` declaration in the compilation that (transitively) implements
  `Quark.Core.Abstractions.Grains.IGrain` or `IGrainObserver`. An interface implementing both is
  ambiguous and is skipped (mirrors `GrainProxyGenerator`'s own skip rule).
- **Guarded on `Quark.Client` being referenced** — if the compiling project doesn't reference
  `Quark.Client` (e.g. a shared interfaces-only project consumed by both silo and client), no output
  is emitted. Reference `Quark.Client` from whichever project should own the generated registration
  method (typically the client project, or a shared interfaces project if both silo and client need
  local proxies).

## BehaviorRegistrationGenerator

Generates a single `QuarkRegistrations.g.cs` per assembly, containing one `AddMyAssemblyBehaviors()` extension method that registers every behavior, transport dispatcher, and activation memory accessor found in that assembly.

### Input — a behavior class

```csharp
public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
{
    public CounterBehavior(IActivationMemory<CounterState> memory) { ... }
}
```

### Generated output (simplified)

```csharp
// QuarkRegistrations.g.cs
public static class QuarkRegistrations
{
    public static IServiceCollection AddMyAssemblyBehaviors(this IServiceCollection services)
    {
        services.AddGrainBehavior<ICounterGrain, CounterBehavior>();
        services.AddGrainTransportDispatcher(
            new GrainType("CounterGrain"),
            CounterGrainProxy_TransportDispatcher.Instance);
        services.AddScoped<IActivationMemory<CounterState>>(sp =>
            new ActivationMemoryAccessor<CounterState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<CounterState>()));
        return services;
    }
}
```

### Usage

```csharp
// Replaces all per-grain manual registrations:
silo.Services.AddMyAssemblyBehaviors();
```

### Trigger conditions

The generator emits a `BehaviorModel` for any class that:
1. Is not `abstract` or generic
2. Has at least one `public` or `internal` constructor
3. Implements `IGrainBehavior` (directly or transitively)

### Diagnostics

| Code | Meaning |
|---|---|
| `QRK0050` | Behavior class does not implement any `IGrain`-derived interface |
| `QRK0051` | Behavior class implements multiple `IGrain`-derived interfaces — ambiguous; add `[GrainBehavior("typeName")]` |
| `QRK0052` | Two behaviors use `IPersistentState<T>` on the same `T` with conflicting (stateName, providerName) combinations |
| `QRK0053` | Behavior carries `[ImplicitStreamSubscription]` but the assembly does not reference `Quark.Streaming.InMemory` — auto-registration is skipped (warning) |
| `QRK0056` | Behavior implements `IGrainUserServiceProviderFactory` and also uses `IPersistentActivationMemory<T>`/`[PersistentState]` — unsupported combination (v1 limitation) |

### State type detection

The generator scans all constructor parameters and emits scoped registrations automatically for:

| Parameter type | Emitted registration |
|---|---|
| `IActivationMemory<T>` | `ActivationMemoryAccessor<T>` backed by shell holder |
| `IPersistentActivationMemory<T>` | `PersistentActivationMemoryAccessor<T>` backed by `IStorage<T>` |
| `IManagedActivationMemory<T>` | `ManagedActivationMemoryAccessor<T>` backed by managed shell holder |
| `IEagerActivationMemory<T>` | `AddEagerActivationMemory<T>()` (eager shell holder + accessor) |

For `IManagedActivationMemory<T>`, the generated registration is:

```csharp
services.AddScoped<IManagedActivationMemory<RingBuffer>>(static sp =>
    new ManagedActivationMemoryAccessor<RingBuffer>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateManagedHolder<RingBuffer>()));
```

All memory types are deduplicated across behaviors — if two behaviors in the same assembly share the same state type, only one registration is emitted.

### Implicit stream subscriptions

For every `[ImplicitStreamSubscription("ns")]` on a behavior, the generator also emits the
matching subscription registration into `AddMyAssemblyBehaviors()`:

```csharp
// behavior: [ImplicitStreamSubscription("chat")] sealed class RoomGrainBehavior ...
services.AddImplicitStreamSubscription("chat", "RoomGrain");
```

The grain-type key honors `[GrainBehavior("key")]`; multiple `[ImplicitStreamSubscription]`
attributes on one behavior each emit a line. Emission is **guarded**: if the assembly does not
reference `Quark.Streaming.InMemory` (so `AddImplicitStreamSubscription` is unresolvable), the
generator skips emission and reports **QRK0053** instead of emitting code that would not compile.
Add a `Quark.Streaming.InMemory` reference to silence the warning and enable auto-wiring.

### User-service-provider factory registration

When a behavior class implements `IGrainUserServiceProviderFactory`, the generator emits an additional
registration call into `AddMyAssemblyBehaviors()`:

```csharp
// behavior: class MyBehavior : IGrainBehavior, IMyGrain, IGrainUserServiceProviderFactory { ... }
services.AddGrainUserServiceProviderFactory<IMyGrain, MyBehavior>();
```

This registers a **satellite dependency resolver** per grain **type** (not per-activation, not
per-call), allowing the behavior to inject a dedicated `IServiceProvider` — built once at silo
startup and shared by every activation of that grain type — that reuses heavy, stateless user
services across all calls without re-resolving them. See
[`docs/superpowers/specs/2026-07-10-grain-user-service-provider-factory-design.md`](../../docs/superpowers/specs/2026-07-10-grain-user-service-provider-factory-design.md)
for design details.

#### Activation memory accessor registration

Every generated activation-memory accessor (`IActivationMemory<T>`, `IManagedActivationMemory<T>`,
`IEagerActivationMemory<T>`) is registered via `AddQuarkOwnedScoped<T>()` rather than plain
`AddScoped<T>()` — **unconditionally, for every behavior in the assembly**, whether or not that specific
behavior implements `IGrainUserServiceProviderFactory`. This isn't scoped to opted-in behaviors only:
the "Quark-only" satellite provider is built once and shared process-wide (see
[Architecture § Quark-owned services vs. user services](Architecture#quark-owned-services-vs-user-services)),
so every assembly's accessors need to already be replayable onto it the moment *any* behavior in the
process opts in, not just the ones that do. On the default (non-satellite) resolution path,
`AddQuarkOwnedScoped<T>()` performs the exact same `services.AddScoped<T>()` registration under the
hood and additionally captures a replayable marker — so a behavior that never opts in sees no
behavioral difference from plain `AddScoped<T>()`, it's simply always the mechanism used.

**v1 limitation**: `IPersistentActivationMemory<T>` and `[PersistentState]` are not yet supported on
behaviors that implement `IGrainUserServiceProviderFactory`; these features will be enabled in a future
release. The generator reports this combination as a build error (`QRK0056`) rather than letting it fail
at runtime.

**v1 limitation**: `CompositeServiceProvider` (the fallback provider used for opted-in behaviors) does not
merge `IEnumerable<T>` registrations across the Quark-only and user-supplied providers — MS.DI always
returns a non-null collection for `IEnumerable<T>`, so the Quark-only side's (possibly empty) result always
wins and the user provider's registrations for that type are never consulted. If a behavior needs multiple
registered implementations of an interface, aggregate them inside `CreateUserServiceProvider` itself.

## SerializerGenerator

For every type annotated `[GenerateSerializer]`, emits an `IFieldCodec<T>` and `IDeepCopier<T>` implementation.

### Input

```csharp
[GenerateSerializer]
public sealed class ChatMsg
{
    [Id(0)] public string Author  { get; set; } = "";
    [Id(1)] public string Text    { get; set; } = "";
    [Id(2)] public DateTimeOffset Created { get; set; }
}
```

### Generated output (simplified)

```csharp
// ChatMsgCodec.g.cs
[RegisterSerializer]
public sealed class ChatMsgCodec : IFieldCodec<ChatMsg>
{
    // Emits/reads each [Id]-tagged field using the registered primitive codecs
    // No reflection — all field access is direct property get/set
}

// ChatMsgCopier.g.cs
[RegisterCopier]
public sealed class ChatMsgCopier : IDeepCopier<ChatMsg>
{
    public ChatMsg DeepCopy(ChatMsg original, CopyContext context)
        => new ChatMsg
        {
            Author  = original.Author,
            Text    = original.Text,
            Created = original.Created,
        };
}
```

### Rules

- `[Id(uint)]` values must be unique within a type and stable across versions.
- Never reuse or renumber an `[Id]` — removed fields should be tombstoned with a comment, not renumbered.
- Adding new fields with new ids is forwards-compatible (old readers skip unknown ids).
- `[Alias("name")]` provides a stable string alias for polymorphic type resolution.

## Enabling the generators

Add a reference to `Quark.CodeGenerator` in your project file:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Quark.CodeGenerator\Quark.CodeGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

In NuGet-based setups, reference the `Quark.CodeGenerator` package. The generators run automatically as part of `dotnet build`.
