---
name: quark-writing-grains
description: Use when writing or modifying a Quark grain — defining an IGrain interface, a POCO IGrainBehavior, its activation state, serializable DTOs, placement, or the DI registration that wires them. Quark-specific (engine/behavior model, NOT the Orleans Grain base class).
---

# Writing Quark Grains

## Overview

Quark uses an **engine model**: there is no `Grain` base class. A grain is split into three parts:

1. **Interface** — `interface IFoo : IGrainWith{String|Guid|Integer}Key` with `Task`/`Task<T>` methods.
2. **Behavior** — a `public sealed class FooBehavior : IGrainBehavior, IFoo` POCO. Constructed **per call** from a fresh `IServiceScope`, executes one method, then discarded. Inject everything via the constructor.
3. **State** — survives across calls (lost on deactivation) via `IActivationMemory<TState>`, owned by the long-lived shell.

Cross-call/cross-grain values that travel over TCP must be `[GenerateSerializer]` types. Method calls are serialized by the grain's single-threaded mailbox, so behavior code needs **no locks**.

## Quick reference

| Need | Use |
|---|---|
| Key type | `IGrainWithStringKey` / `IGrainWithGuidKey` / `IGrainWithIntegerKey` |
| Per-call state that survives across calls | `IActivationMemory<TState>` (`.Value`) |
| Grain identity inside a behavior | `ICallContext ctx` → `ctx.GrainId.Key` |
| Activation/deactivation hooks | implement `IActivationLifecycle` |
| Call another grain | inject `IGrainFactory factory` → `factory.GetGrain<IOther>(key)` |
| Register a timer | inject `IActivationShellAccessor` → `shell.Shell.RegisterTimer<TState>(...)` |
| Placement | attribute on the **behavior class** (see quark-host-setup) |

## Canonical template

```csharp
// --- IFoo.cs (GrainInterfaces project) ---
using Quark.Core.Abstractions.Grains;

public interface IFooGrain : IGrainWithStringKey
{
    Task<int> IncrementAsync(int by);
    Task<int> GetAsync();
}

// --- FooState.cs (Grains project) — only [GenerateSerializer] if it crosses TCP or is persisted ---
using Quark.Serialization.Abstractions.Attributes;

public sealed class FooState   // plain activation state; no attribute needed if never serialized
{
    public int Count { get; set; }
}

// --- FooBehavior.cs (Grains project) ---
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;   // ICallContext, IActivationShellAccessor

public sealed class FooBehavior : IGrainBehavior, IFooGrain
{
    private readonly IActivationMemory<FooState> _memory;
    private readonly ICallContext _ctx;

    public FooBehavior(IActivationMemory<FooState> memory, ICallContext ctx)
    {
        _memory = memory;
        _ctx = ctx;
    }

    private FooState S => _memory.Value;   // shared idiom across all Quark samples

    public Task<int> IncrementAsync(int by) { S.Count += by; return Task.FromResult(S.Count); }
    public Task<int> GetAsync() => Task.FromResult(S.Count);
}
```

### With activation lifecycle (load state, register a timer)

```csharp
public sealed class FooBehavior : IGrainBehavior, IFooGrain, IActivationLifecycle
{
    public Task OnActivateAsync(CancellationToken ct) { /* init */ return Task.CompletedTask; }
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        S.Timer?.Dispose();           // dispose timers/handles you created
        return Task.CompletedTask;
    }
}
```

## DI registration

**Preferred — let the generators do it.** Reference `Quark.CodeGenerator` as an analyzer in the Grains and GrainInterfaces csproj. Then:
- `BehaviorRegistrationGenerator` emits `Add{AssemblyName}Behaviors()` (registers every behavior, its transport dispatcher, and `IActivationMemory<T>` accessors). Call it in the silo: `silo.Services.AddMyGrainsBehaviors();`
- `ClientProxyRegistrationGenerator` (in the interfaces assembly) emits `Add{InterfaceAssembly}GrainProxies()`. Call it on the client: `client.Services.AddMyGrainInterfacesGrainProxies();`

**Manual (tests / pre-generator)** — register the behavior, its transport dispatcher (named by `GrainType`), and the activation-memory accessor explicitly:

```csharp
// silo
silo.Services.AddGrainBehavior<IFooGrain, FooBehavior>();
silo.Services.AddGrainTransportDispatcher(
    new GrainType("FooGrain"),                    // grain type name = interface name minus leading 'I'
    new FooGrainProxy_TransportDispatcher());     // generated, or hand-written in tests
silo.Services.AddScoped<IActivationMemory<FooState>>(sp =>
    new ActivationMemoryAccessor<FooState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<FooState>()));

// client
client.Services.AddGrainProxy<IFooGrain, FooGrainProxy>();
```

`AddGrainBehavior`/`AddGrainTransportDispatcher`/`ActivationMemoryAccessor` live in `Quark.Runtime`; `GrainType` in `Quark.Core.Abstractions.Identity` (a global using in runtime projects). See quark-testing for the TestCluster wrapper around this.

## Serialization rule

Apply `[GenerateSerializer]` + stable `[Id(uint)]` on every member of any type that **crosses a TCP grain-call boundary or is persisted**. In-process calls never serialize. Enums work as method params/returns and as members. IDs must be stable forever — never reuse or renumber. Add `[Alias("name")]` for type-level versioning.

```csharp
[GenerateSerializer]
[Alias("FooDto")]
public sealed class FooDto
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Value { get; set; }
}
```

## Common mistakes

- **Storing state in behavior fields.** Behaviors are per-call and discarded — fields don't persist. Put cross-call state in `IActivationMemory<TState>`.
- **Using `IGrainContext`.** It is stale/unwired. Use `ICallContext` for per-call identity.
- **Adding a constructor with no DI.** Everything (memory, factory, context, providers, loggers) comes through the constructor from the scope.
- **Forgetting `[GenerateSerializer]`** on a type used in a remote call → `CodecNotFoundException` / `NotSupportedException` at TCP runtime (in-process tests won't catch it).
- **Locking in behavior code.** Unnecessary — the mailbox serializes calls per grain.
- **Redundant usings.** `Quark.Core.Abstractions.Identity` is a global using in runtime projects; don't re-add it.

## Related skills

- quark-persistence — durable state (the 5 patterns)
- quark-streaming — publish/subscribe streams from a behavior
- quark-host-setup — silo/client wiring, placement, timers, reminders
- quark-testing — TestCluster + hand-wired registration for unit tests
