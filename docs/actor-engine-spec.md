# Actor Engine — Design Specification

**Issue:** #6  
**Status:** Design  
**Milestone:** M2 (Engine Kernel)  
**Scope:** Clean break from the Orleans-style `Grain` base class. AOT-first throughout. No backward compatibility path. No hot-reload (use .NET's built-in MetadataUpdate mechanism).

---

## 1. Vision

Quark moves from a **Framework model** to an **Engine model**.

| | Framework model (current) | Engine model (target) |
|---|---|---|
| Developer writes | `class MyGrain : Grain` | `class MyBehavior : IGrainBehavior` |
| Lifecycle owner | Developer (inherits base class) | Engine (controls all scope and scheduling) |
| DI scope | Root — leaks scoped services into long-lived objects | Per-call — every call gets a fresh `IServiceScope` |
| Scoped DI (DbContext etc.) | Manual workaround or leaked | Injected directly into constructor |
| Multi-tenancy | AsyncLocal / manual stamping | Set once in scope before behavior is constructed |
| In-memory state | Field on the long-lived grain | `IActivationMemory<TState>` — lives in shell, injected per call |
| Persistent state | `Grain<TState>` base class | `IPersistentActivationMemory<TState>` — shell cache + storage write-through |
| RAM footprint | Full grain object tree per activation | Lightweight shell only; behavior objects exist for milliseconds |
| Startup safety | DI misconfiguration fails at first live call | `BehaviorStartupValidator` fails the silo at startup |

The central contract:

> The `GrainActivation` **shell** owns the mailbox and lives indefinitely.  
> A **behavior component** is resolved fresh from DI on every call, executes, and is discarded.

---

## 2. Design Principles

1. **AOT-first.** Every type resolved from DI, every cast, every generic instantiation must be visible to the AOT linker at publish time. No `Type.GetType(string)`, no `MakeGenericType`, no reflection-based construction on any hot path.
2. **Mailbox unchanged.** The `Channel<Func<Task>>` single-reader queue is the actor model's correctness guarantee. It is not touched by this work.
3. **No byte serialization on local calls.** The typed `IGrainInvokable<TResult>` struct pattern stays. Arguments are never serialized to bytes for in-process dispatch.
4. **Clean break.** The `Grain` base class, `GrainActivatorGenerator`, and `IGrainActivatorFactory` are removed. There is no coexistence path.
5. **Fail fast.** Any DI misconfiguration in a behavior is detected at silo startup by the `BehaviorStartupValidator`, not on the first live production call.
6. **Single responsibility per layer.** The shell knows nothing about business logic. The behavior knows nothing about scheduling. The scope knows nothing about persistence.

---

## 3. Glossary

| Term | Definition |
|---|---|
| **Shell** | The `GrainActivation` instance in `GrainActivationTable`. Owns the mailbox. Holds `GrainId`, root `IServiceProvider`, and `StateHolder<TState>` bags for activation memory. |
| **Behavior** | A POCO class implementing `IGrainBehavior`. Instantiated per call inside a short-lived `IServiceScope`. |
| **Call scope** | An `IServiceScope` created at the start of every grain method call, used to resolve the behavior and all its dependencies, then disposed when the call returns. |
| **Activation memory** | A `StateHolder<TState>` owned by the shell. Survives across calls on the same activation; lost on deactivation. Exposed to behavior via `IActivationMemory<TState>`. |
| **Persistent activation memory** | `IActivationMemory<TState>` extended with load/save hooks against `IGrainStorage`. Replaces `Grain<TState>`. |

---

## 4. Architecture

```
Caller
  │
  ▼
IGrainCallInvoker.InvokeAsync(grainId, invokable)
  │
  ├─ TryRouteRemote()  ──── TCP transport (unchanged)
  │
  └─ GetOrActivateAsync(grainId)
         │
         ▼
  ┌────────────────────────────────────────────────────────┐
  │ GrainActivation (shell — long-lived)                   │
  │                                                         │
  │  GrainId  GrainType  IServiceProvider _root            │
  │  Channel<Func<Task>> _queue  (mailbox)                 │
  │  ConcurrentDictionary<Type, StateHolder> _memoryBag    │
  └──────────────────────┬─────────────────────────────────┘
                         │ PostAsync(work)
                         ▼ (single-threaded turn)
             ┌───────────────────────────────────┐
             │  IServiceScope  (per-call)        │
             │                                   │
             │  IActivationShellAccessor ────────┼──► shell
             │  ICallContextSetter       ────────┼──► GrainId stamped in
             │  IBehaviorResolver        ────────┼──► reads IGrainTypeRegistry
             │                                   │
             │  MyBehavior (POCO)                │  ← constructed here
             │    IActivationMemory<TState>      │  ← wraps shell's StateHolder
             │    IPersistentActivationMemory<T> │  ← + storage write-through
             │    ICallContext                   │  ← this call's identity
             │    DbContext / etc.               │  ← scoped DI, safe to inject
             └────────────────┬──────────────────┘
                              │
                              ▼
               invokable.Invoke(behavior)   ← typed call, zero alloc
                              │
                              ▼
               result returned to caller
               scope.Dispose()             ← DbContext, scoped services freed
```

---

## 5. Core Abstractions  (`Quark.Core.Abstractions`)

### 5.1 `IGrainBehavior`

```csharp
/// <summary>
/// Marker interface for a grain behavior component.
/// Implement on a POCO class — no base class required.
/// One instance is constructed per grain method call inside a short-lived DI scope.
/// Constructor parameters are resolved from that scope.
/// </summary>
public interface IGrainBehavior { }
```

### 5.2 `[GrainBehavior]`

```csharp
/// <summary>
/// Declares the stable string ID that maps this behavior class to a grain interface.
/// Applied by the code generator; can also be applied manually on hand-authored behaviors.
/// The ID must be stable across deployments — it is used as the grain type key.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GrainBehaviorAttribute(string behaviorId) : Attribute
{
    public string BehaviorId { get; } = behaviorId;
}
```

### 5.3 `ICallContext` / `ICallContextSetter`

```csharp
/// <summary>
/// Ambient per-call context. Registered as Scoped; available to behavior constructors
/// and any service they inject. Carries the identity of the grain being called.
/// </summary>
public interface ICallContext
{
    GrainId GrainId { get; }
}

/// <summary>
/// Engine-internal. Used by LocalGrainCallInvoker to stamp the GrainId
/// into the scope before the behavior is constructed.
/// </summary>
public interface ICallContextSetter
{
    void Set(GrainId grainId);
}
```

Both are implemented by a single `sealed class CallContext` registered as `Scoped`:

```csharp
internal sealed class CallContext : ICallContext, ICallContextSetter
{
    public GrainId GrainId { get; private set; }
    public void Set(GrainId grainId) => GrainId = grainId;
}
```

> **Multi-tenancy extension point.** A consuming application registers its own scoped `ITenantContext` service that reads from `ICallContext.GrainId` or from a call-chain `AsyncLocal`. Quark does not own tenant infrastructure — it only guarantees a fresh scope per call where such infrastructure can safely live.

### 5.4 `IActivationMemory<TState>`

```csharp
/// <summary>
/// Provides access to in-memory state that lives in the GrainActivation shell.
/// The TState value is created once per activation and reused across all calls.
/// NOT persisted — lost when the activation deactivates or the silo restarts.
/// Use IPersistentActivationMemory<TState> for durable state.
///
/// Thread-safety: mutations are safe only from within the grain's mailbox.
/// Do not mutate Value from timer callbacks or external threads without
/// routing through GrainActivation.PostAsync.
/// </summary>
public interface IActivationMemory<TState>
    where TState : class, new()
{
    TState Value { get; }
}
```

### 5.5 `IActivationLifecycle`

```csharp
/// <summary>
/// Optional interface for behaviors that need to run logic on first activation
/// or before deactivation. Both hooks run on the grain's mailbox thread.
/// The engine resolves a fresh behavior instance via the same per-call scope
/// mechanism to invoke these hooks.
/// </summary>
public interface IActivationLifecycle : IGrainBehavior
{
    Task OnActivateAsync(CancellationToken ct);
    Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct);
}
```

---

## 6. Persistent Activation Memory  (`Quark.Persistence.Abstractions`)

`IPersistentActivationMemory<TState>` replaces `Grain<TState>`. It is a shell-level cache of durable state: loaded once on activation, written explicitly, and backed by `IGrainStorage`.

### 6.1 Interface

```csharp
/// <summary>
/// Combines IActivationMemory<TState> with IGrainStorage read/write.
/// Value lives in the GrainActivation shell across calls.
/// LoadAsync/SaveAsync/ClearAsync delegate to the registered IGrainStorage provider.
///
/// Typical usage:
///   - Call LoadAsync() in IActivationLifecycle.OnActivateAsync.
///   - Read Value freely on any call (no storage round-trip).
///   - Call SaveAsync() after mutations that must survive deactivation.
/// </summary>
public interface IPersistentActivationMemory<TState>
    where TState : class, new()
{
    /// <summary>Current in-memory state. Initially a default-constructed instance.</summary>
    TState Value { get; }

    /// <summary>Loads state from IGrainStorage into Value. Call once in OnActivateAsync.</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Persists Value to IGrainStorage.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Clears persisted state and resets Value to a new default instance.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
```

### 6.2 Implementation

`IPersistentActivationMemory<TState>` is backed by a `StateHolder<TState>` in the shell's memory bag (same mechanism as `IActivationMemory<TState>`). The holder is shared: both interfaces point to the same object, so a call using `IActivationMemory<TState>` and one using `IPersistentActivationMemory<TState>` see the same state.

```csharp
// Internal — one instance per (activation, TState) pair
internal sealed class StateHolder<TState> where TState : class, new()
{
    public TState Value { get; set; } = new();
}

// IActivationMemory<TState> implementation
internal sealed class ActivationMemoryAccessor<TState>(StateHolder<TState> holder)
    : IActivationMemory<TState> where TState : class, new()
{
    public TState Value => holder.Value;
}

// IPersistentActivationMemory<TState> implementation (Quark.Persistence.Abstractions)
internal sealed class PersistentActivationMemoryAccessor<TState>(
    StateHolder<TState> holder,
    IStorage<TState> storage,
    ICallContext ctx,
    string stateName) : IPersistentActivationMemory<TState>
    where TState : class, new()
{
    public TState Value => holder.Value;

    public async Task LoadAsync(CancellationToken ct = default)
        => holder.Value = await storage.ReadAsync(ctx.GrainId, stateName, ct).ConfigureAwait(false);

    public Task SaveAsync(CancellationToken ct = default)
        => storage.WriteAsync(ctx.GrainId, holder.Value, stateName, ct);

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await storage.ClearAsync(ctx.GrainId, stateName, ct).ConfigureAwait(false);
        holder.Value = new TState();
    }
}
```

`GrainActivation.GetOrCreateHolder<TState>()` replaces the raw `GetOrCreateMemory<TState>()`:

```csharp
// Inside GrainActivation
private readonly ConcurrentDictionary<Type, object> _memoryBag = new();

internal StateHolder<TState> GetOrCreateHolder<TState>() where TState : class, new()
    => (StateHolder<TState>)_memoryBag.GetOrAdd(typeof(TState), static _ => new StateHolder<TState>());
```

Both `ActivationMemoryAccessor<TState>` and `PersistentActivationMemoryAccessor<TState>` receive the same `StateHolder<TState>` instance, so mutations from either interface are immediately visible through the other.

### 6.3 DI registration (emitted by code generator)

```csharp
// For behaviors using IActivationMemory<OrderState>:
services.AddScoped<IActivationMemory<OrderState>>(sp =>
    new ActivationMemoryAccessor<OrderState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<OrderState>()));

// For behaviors using IPersistentActivationMemory<OrderState>:
services.AddScoped<IPersistentActivationMemory<OrderState>>(sp =>
    new PersistentActivationMemoryAccessor<OrderState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<OrderState>(),
        sp.GetRequiredService<IStorage<OrderState>>(),
        sp.GetRequiredService<ICallContext>(),
        StorageOptions.DefaultStateName));
```

All registrations are closed-generic. The AOT linker sees every `TState` statically at compile time.

---

## 7. Engine Kernel  (`Quark.Runtime`)

### 7.1 `IActivationShellAccessor`

The bridge between the per-call scope and the long-lived shell. Set by `LocalGrainCallInvoker` immediately after scope creation, before the behavior is resolved.

```csharp
public interface IActivationShellAccessor
{
    GrainActivation Shell { get; }
}

internal sealed class ActivationShellAccessor : IActivationShellAccessor
{
    public GrainActivation Shell { get; set; } = null!;
}
```

### 7.2 `IBehaviorResolver`

```csharp
/// <summary>
/// Scoped service. Resolves the behavior instance for the current call.
/// Reads the behavior class from IGrainTypeRegistry and constructs it
/// via ActivatorUtilities (AOT-safe when types are statically registered).
/// </summary>
public interface IBehaviorResolver
{
    IGrainBehavior Resolve(GrainType grainType);
}

internal sealed class BehaviorResolver(
    IServiceProvider scope,
    IGrainTypeRegistry typeRegistry) : IBehaviorResolver
{
    public IGrainBehavior Resolve(GrainType grainType)
    {
        if (!typeRegistry.TryGetGrainClass(grainType, out Type? type) || type is null)
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");

        return (IGrainBehavior)ActivatorUtilities.CreateInstance(scope, type);
    }
}
```

### 7.3 `GrainActivation` changes

**Remove:**
- `Grain Grain` public property
- `GrainContext Context`

**Add:**
- `IServiceProvider _root`
- `GrainType GrainType`
- `ConcurrentDictionary<Type, object> _memoryBag` + `GetOrCreateHolder<TState>()`

**Unchanged:** mailbox (`_queue`), `PostAsync`, `IsIdleLongerThan`, `SetOnDeactivated`, `DisposeAsync`, reentrant flag.

```csharp
public sealed class GrainActivation : IAsyncDisposable
{
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<Type, object> _memoryBag = new();
    private readonly bool _isReentrant;
    private readonly ILogger<GrainActivation> _logger;
    private readonly Task _processingLoop;
    private readonly CancellationTokenSource _cts = new();
    private Func<Task>? _onDeactivated;
    private long _lastAccessedTicks;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    public GrainId GrainId { get; }
    public GrainType GrainType { get; }

    internal GrainActivation(
        GrainId grainId,
        GrainType grainType,
        bool isReentrant,
        IServiceProvider root,
        ILogger<GrainActivation> logger)
    {
        GrainId = grainId;
        GrainType = grainType;
        _isReentrant = isReentrant;
        _root = root;
        _logger = logger;
        _processingLoop = RunLoopAsync(_cts.Token);
        _lastAccessedTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    internal StateHolder<TState> GetOrCreateHolder<TState>() where TState : class, new()
        => (StateHolder<TState>)_memoryBag.GetOrAdd(typeof(TState), static _ => new StateHolder<TState>());

    // PostAsync, RunLoopAsync, IsIdleLongerThan, SetOnDeactivated, DisposeAsync — structure unchanged
}
```

### 7.4 `LocalGrainCallInvoker` — per-call scope

```csharp
await activation.PostAsync(async () =>
{
    using IServiceScope scope = _root.CreateScope();
    IServiceProvider sp = scope.ServiceProvider;

    // 1. Bind shell so IActivationMemory<T> registrations reach the memory bag
    ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = activation;

    // 2. Stamp per-call identity before behavior is constructed
    sp.GetRequiredService<ICallContextSetter>().Set(activation.GrainId);

    // 3. Resolve behavior — type from IGrainTypeRegistry, constructed by ActivatorUtilities
    IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>()
                                .Resolve(activation.GrainType);
    try
    {
        TResult result = await invokable.Invoke(behavior).ConfigureAwait(false);

        if (_copierProvider?.TryGetCopier<TResult>() is { } copier)
            result = copier.DeepCopy(result, new CopyContext());

        tcs.TrySetResult(result);
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
    // scope.Dispose() → scoped services (DbContext, etc.) freed here
});
```

### 7.5 `IGrainInvokable` signature change

```csharp
public interface IGrainInvokable<TResult>
{
    uint MethodId { get; }
    ValueTask<TResult> Invoke(IGrainBehavior behavior);  // was: Invoke(Grain grain)
    void Serialize(ref CodecWriter writer);
}

public interface IGrainVoidInvokable
{
    uint MethodId { get; }
    ValueTask Invoke(IGrainBehavior behavior);           // was: Invoke(Grain grain)
    void Serialize(ref CodecWriter writer);
}
```

The cast inside each generated struct is to a statically known interface — AOT-linker-visible:

```csharp
// Generated
public readonly struct Counter_IncrementAsync_Invokable : IGrainVoidInvokable
{
    public uint MethodId => 1u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((ICounter)behavior).IncrementAsync();
    public void Serialize(ref CodecWriter writer) { }
}
```

### 7.6 `IActivationLifecycle` dispatch

On shell creation, `LocalGrainCallInvoker` posts an activation work item using the same scope creation pattern. On disposal, `GrainActivation.DisposeAsync` posts a final deactivation work item. Both resolve a behavior instance from a fresh scope and check for `IActivationLifecycle`:

```csharp
private async Task RunLifecycleHookAsync(
    GrainActivation activation,
    Func<IActivationLifecycle, Task> hook,
    CancellationToken ct)
{
    using IServiceScope scope = _root.CreateScope();
    IServiceProvider sp = scope.ServiceProvider;

    ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = activation;
    sp.GetRequiredService<ICallContextSetter>().Set(activation.GrainId);

    IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>()
                                .Resolve(activation.GrainType);

    if (behavior is IActivationLifecycle lifecycle)
        await hook(lifecycle).ConfigureAwait(false);
}
```

### 7.7 Timer wiring

Timers are registered on the shell. The callback posts a work item to the mailbox and creates its own call scope — identical to a regular method call:

```csharp
// Timer callback posts to mailbox, resolves behavior, executes
_root.CreateScope() → stamp → resolve → timerHandler(behavior, state, ct) → dispose
```

`ITimerHandler<TState>` is an optional interface that behaviors implement to receive timer ticks. The code generator emits a timer trampoline when it detects a behavior method annotated with `[GrainTimer]` (forward-compatible extension, not required for M2).

### 7.8 `BehaviorStartupValidator`

Registered as a mandatory `IHostedService` by `AddQuarkRuntime()`. Fails silo startup if any behavior's constructor dependencies are not satisfied.

```csharp
internal sealed class BehaviorStartupValidator(
    IGrainTypeRegistry typeRegistry,
    IServiceProvider root,
    ILogger<BehaviorStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        foreach ((GrainType grainType, Type behaviorType) in typeRegistry.GetAllRegistrations())
        {
            try
            {
                using IServiceScope scope = root.CreateScope();
                IServiceProvider sp = scope.ServiceProvider;

                // Provide minimal valid context for the probe
                var probeId = GrainId.Create(grainType, "startup-validation-probe");
                ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>())
                    .Shell = GrainActivation.CreateProbe(probeId, grainType, root);
                sp.GetRequiredService<ICallContextSetter>().Set(probeId);

                ActivatorUtilities.CreateInstance(sp, behaviorType);

                logger.LogDebug("Behavior {Type} DI validated", behaviorType.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Silo startup aborted: behavior '{behaviorType.FullName}' failed DI validation. " +
                    $"Ensure all constructor dependencies are registered in the silo's DI container.",
                    ex);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

`GrainActivation.CreateProbe` is a lightweight shell with an empty memory bag, used only for startup validation. It does not start a processing loop.

### 7.9 Observer compatibility

`IObserverVoidInvokable.Invoke(object target)` already accepts `object`. The `ObserverRegistry` stores plain `object` references. Observers are **not** affected by M2 and remain as plain objects.

Observer alignment to `IGrainBehavior` — including per-call scope resolution and `ICallContext` injection for observer targets — is deferred to **M-Observer** (a named future milestone). M2 must not break the existing observer dispatch path.

---

## 8. Code Generator  (`Quark.CodeGenerator`)

### Removed

`GrainActivatorGenerator` — the entire generator is deleted.

### Changed: `GrainProxyGenerator`

Generated invokable structs change `Invoke(Grain grain)` → `Invoke(IGrainBehavior behavior)`.

### Changed: startup registration

```csharp
// Before (framework model)
services.AddGrain<CounterGrain>();
services.AddGrainMethodInvoker<CounterGrain, CounterGrainMethodInvoker>();
services.AddGrainActivatorFactory<CounterGrainActivatorFactory>();

// After (engine model)
services.AddGrainBehavior<ICounter, CounterBehavior>();
services.AddGrainMethodInvoker<CounterBehavior, CounterBehaviorMethodInvoker>();
```

`AddGrainBehavior<TInterface, TBehavior>()`:
1. Registers `TBehavior` as `Transient` (required by `ActivatorUtilities`)
2. Registers `GrainType → typeof(TBehavior)` in `IGrainTypeRegistry`
3. Derives grain type string from `[GrainBehavior]` on `TBehavior`, or falls back to `typeof(TInterface).Name[1..]`

### New: per-assembly `AddGrainBehaviorRegistrations()` extension

Each assembly that contains behaviors has the code generator as an analyzer reference. The generator emits a single `QuarkRegistrations` partial class in that assembly with one extension method:

```csharp
// Generated in MyDomain.dll — called by the host silo builder
public static partial class QuarkRegistrations
{
    public static IServiceCollection AddMyDomainBehaviors(this IServiceCollection services)
    {
        // Behavior registrations
        services.AddGrainBehavior<ICounter, CounterBehavior>();
        services.AddGrainBehavior<IOrder, OrderBehavior>();
        services.AddGrainMethodInvoker<CounterBehavior, CounterBehaviorMethodInvoker>();
        services.AddGrainMethodInvoker<OrderBehavior, OrderBehaviorMethodInvoker>();

        // Closed-generic IActivationMemory<T> registrations — one per distinct TState
        services.AddScoped<IActivationMemory<CounterState>>(sp =>
            new ActivationMemoryAccessor<CounterState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<CounterState>()));

        // Closed-generic IPersistentActivationMemory<T> registrations
        services.AddScoped<IPersistentActivationMemory<OrderState>>(sp =>
            new PersistentActivationMemoryAccessor<OrderState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<OrderState>(),
                sp.GetRequiredService<IStorage<OrderState>>(),
                sp.GetRequiredService<ICallContext>(),
                StorageOptions.DefaultStateName));

        return services;
    }
}
```

The host silo builder calls each domain assembly's extension:

```csharp
builder.UseQuark(silo =>
{
    silo.Services.AddQuarkRuntime();
    silo.Services.AddMyDomainBehaviors();       // from MyDomain.dll
    silo.Services.AddInventoryBehaviors();      // from Inventory.dll
});
```

This keeps all DI registrations explicit and statically visible to the AOT linker. No assembly scanning, no reflection at runtime.

---

## 9. Package Layout Changes

### `Quark.Core.Abstractions`

Add:
- `Grains/IGrainBehavior.cs`
- `Grains/IActivationLifecycle.cs`
- `Grains/GrainBehaviorAttribute.cs`
- `Hosting/ICallContext.cs`
- `Hosting/ICallContextSetter.cs`
- `Hosting/IActivationMemory.cs`

Remove:
- `Grains/Grain.cs`

### `Quark.Persistence.Abstractions`

Add:
- `IPersistentActivationMemory.cs`
- `StateHolder.cs` (internal)
- `PersistentActivationMemoryAccessor.cs` (internal)

Remove:
- `Grain.cs` (`Grain<TState>` base class)

### `Quark.Runtime`

Add:
- `BehaviorResolver.cs`
- `ActivationShellAccessor.cs`
- `ActivationMemoryAccessor.cs` (internal)
- `BehaviorStartupValidator.cs`

Change:
- `GrainActivation.cs` — remove `Grain` field, add `_root`, `GrainType`, `_memoryBag`, `GetOrCreateHolder<T>()`, `CreateProbe()`
- `LocalGrainCallInvoker.cs` — per-call scope creation, shell binding, behavior resolution, lifecycle hooks
- `RuntimeServiceCollectionExtensions.cs` — register new scoped services + `BehaviorStartupValidator`

Remove:
- All references to `IGrainActivator`, `IGrainActivatorFactory`

### `Quark.Core.Abstractions` — `IGrainInvokable.cs`

- `Invoke(Grain grain)` → `Invoke(IGrainBehavior behavior)` on both `IGrainInvokable<TResult>` and `IGrainVoidInvokable`

### `Quark.CodeGenerator`

- Delete `GrainActivatorGenerator.cs`
- Update `GrainProxyGenerator.cs` — `Invoke` parameter, emit `[GrainBehavior]`, emit `AddGrainBehaviorRegistrations()`
- Add per-assembly `QuarkRegistrations` partial class emission

### `Quark.Testing`

- Hand-written invokers update `Invoke(Grain grain)` → `Invoke(IGrainBehavior behavior)`
- `TestCluster` / `TestSilo` setup updated to use `AddGrainBehavior<>()` pattern

---

## 10. Full API Surface Reference

```csharp
// Quark.Core.Abstractions

public interface IGrainBehavior { }

public interface IActivationLifecycle : IGrainBehavior
{
    Task OnActivateAsync(CancellationToken ct);
    Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GrainBehaviorAttribute(string behaviorId) : Attribute
{
    public string BehaviorId { get; } = behaviorId;
}

public interface ICallContext        { GrainId GrainId { get; } }
public interface ICallContextSetter  { void Set(GrainId grainId); }  // engine-internal

public interface IActivationMemory<TState> where TState : class, new()
{
    TState Value { get; }
}

// Changed signatures:
public interface IGrainInvokable<TResult>
{
    uint MethodId { get; }
    ValueTask<TResult> Invoke(IGrainBehavior behavior);
    void Serialize(ref CodecWriter writer);
}

public interface IGrainVoidInvokable
{
    uint MethodId { get; }
    ValueTask Invoke(IGrainBehavior behavior);
    void Serialize(ref CodecWriter writer);
}

// Quark.Persistence.Abstractions

public interface IPersistentActivationMemory<TState> where TState : class, new()
{
    TState Value { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

// Quark.Runtime

public interface IBehaviorResolver           { IGrainBehavior Resolve(GrainType grainType); }
public interface IActivationShellAccessor    { GrainActivation Shell { get; } }  // engine-internal
```

---

## 11. Migration Guide

This is a **breaking change**. All consumer code must be updated.

### Stateless behavior

```csharp
// Before
public class GreeterGrain : Grain, IGreeter
{
    public Task<string> GreetAsync(string name) => Task.FromResult($"Hello, {name}!");
}

// After
[GrainBehavior("Greeter")]
public sealed class GreeterBehavior : IGrainBehavior, IGreeter
{
    public Task<string> GreetAsync(string name) => Task.FromResult($"Hello, {name}!");
}
```

### In-memory state (non-persistent)

```csharp
// Before
public class CounterGrain : Grain, ICounter
{
    private int _count;
    public Task<int> IncrementAsync() { _count++; return Task.FromResult(_count); }
}

// After
public sealed class CounterState { public int Count; }

[GrainBehavior("Counter")]
public sealed class CounterBehavior : IGrainBehavior, ICounter
{
    private readonly IActivationMemory<CounterState> _mem;
    public CounterBehavior(IActivationMemory<CounterState> mem) => _mem = mem;
    public Task<int> IncrementAsync() { _mem.Value.Count++; return Task.FromResult(_mem.Value.Count); }
}
```

### Persistent state

```csharp
// Before
public class OrderGrain : Grain<OrderState>, IOrder
{
    public async Task ConfirmAsync() { State.Confirmed = true; await WriteStateAsync(); }
    public override Task OnActivateAsync(CancellationToken ct) => base.OnActivateAsync(ct);
}

// After
[GrainBehavior("Order")]
public sealed class OrderBehavior : IGrainBehavior, IOrder, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<OrderState> _state;
    public OrderBehavior(IPersistentActivationMemory<OrderState> state) => _state = state;

    public Task OnActivateAsync(CancellationToken ct) => _state.LoadAsync(ct);
    public Task OnDeactivateAsync(DeactivationReason r, CancellationToken ct) => Task.CompletedTask;

    public async Task ConfirmAsync()
    {
        _state.Value.Confirmed = true;
        await _state.SaveAsync();
    }
}
```

### Scoped DI (DbContext, per-request services)

```csharp
// Before — leaked into root scope
public class ReportGrain : Grain, IReport
{
    public Task<Report> GetAsync()
    {
        using var db = ServiceProvider.GetRequiredService<AppDb>();
        return db.Reports.FirstAsync();
    }
}

// After — naturally scoped, safely disposed
[GrainBehavior("Report")]
public sealed class ReportBehavior : IGrainBehavior, IReport
{
    private readonly AppDb _db;
    public ReportBehavior(AppDb db) => _db = db;  // AppDb registered as Scoped
    public Task<Report> GetAsync() => _db.Reports.FirstAsync();
    // scope.Dispose() calls _db.Dispose() automatically
}
```

### DI registration

```csharp
// Before
silo.Services.AddGrain<OrderGrain>();

// After — called via generated extension per domain assembly
silo.Services.AddMyDomainBehaviors();  // generated, registers AddGrainBehavior<IOrder, OrderBehavior>() etc.
```

---

## 12. Acceptance Criteria

**Core engine (M2)**
- [ ] `IGrainBehavior`, `IActivationLifecycle`, `[GrainBehavior]`, `ICallContext`, `ICallContextSetter`, `IActivationMemory<T>` defined in `Quark.Core.Abstractions`
- [ ] `IGrainInvokable<TResult>.Invoke` and `IGrainVoidInvokable.Invoke` accept `IGrainBehavior`
- [ ] `GrainActivation` holds no `Grain` reference; holds `GrainType`, `IServiceProvider _root`, `_memoryBag` with `StateHolder<T>`
- [ ] `LocalGrainCallInvoker` creates `IServiceScope` per call; binds `IActivationShellAccessor`; stamps `ICallContextSetter`; resolves behavior via `IBehaviorResolver`; disposes scope on completion
- [ ] `IBehaviorResolver`, `IActivationShellAccessor` defined and registered in `Quark.Runtime`
- [ ] `ICallContext.GrainId` is correct inside every behavior call
- [ ] `IActivationMemory<TState>`: same `TState` instance across multiple calls to the same activation; different activations return independent instances
- [ ] `IActivationLifecycle.OnActivateAsync` called exactly once on first shell creation
- [ ] `IActivationLifecycle.OnDeactivateAsync` called exactly once during shell disposal
- [ ] `GrainActivatorGenerator` deleted; no `IGrainActivatorFactory` types remain in solution
- [ ] `Grain` base class removed from `Quark.Core.Abstractions` and `Quark.Persistence.Abstractions`

**Persistent activation memory**
- [ ] `IPersistentActivationMemory<TState>` defined in `Quark.Persistence.Abstractions`
- [ ] `LoadAsync` reads from `IGrainStorage` and updates `StateHolder.Value`
- [ ] `SaveAsync` writes `StateHolder.Value` to `IGrainStorage`
- [ ] `ClearAsync` clears storage and resets `StateHolder.Value` to `new TState()`
- [ ] `IActivationMemory<TState>` and `IPersistentActivationMemory<TState>` share the same `StateHolder<TState>` instance within one activation
- [ ] `Grain<TState>` base class removed

**Code generator**
- [ ] `AddGrainBehavior<TInterface, TBehavior>()` emitted per behavior
- [ ] Closed-generic `IActivationMemory<TState>` and `IPersistentActivationMemory<TState>` scoped registrations emitted per distinct `TState`
- [ ] Per-assembly `AddXxxBehaviors()` extension method generated; host silo builder wires it correctly
- [ ] No open-generic DI registrations emitted

**Startup validator**
- [ ] `BehaviorStartupValidator` registered as `IHostedService` by `AddQuarkRuntime()`
- [ ] Silo refuses to start (throws `InvalidOperationException` with behavior type name) if any behavior constructor dependency is missing
- [ ] Validator uses `GrainActivation.CreateProbe` — does not start a processing loop or interfere with the real activation table

**AOT**
- [ ] `dotnet publish -r linux-x64 /p:PublishAot=true` on a sample project using engine behaviors produces zero AOT warnings
- [ ] No open-generic registrations; no `MakeGenericType`; no `Type.GetType(string)` in Quark-owned hot paths

**Tests**
- [ ] Unit: behavior resolved per call (separate instances per call); scoped DI disposed after call; activation memory persists across calls on same activation; call context populated correctly
- [ ] Unit: `IPersistentActivationMemory<T>` load/save/clear round-trips through `IStorage<T>` mock
- [ ] Unit: `BehaviorStartupValidator` throws on missing dependency; passes on valid registration
- [ ] Unit: `IActivationLifecycle` hooks called correct number of times during full activation/deactivation cycle
- [ ] Integration: full silo start → grain call → state persisted → silo restart → state reloaded via `OnActivateAsync`
