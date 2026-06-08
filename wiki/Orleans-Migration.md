# Orleans Migration Guide

This page covers what changes when moving an Orleans application to Quark, organized by compatibility tier.

## Drop-in (no code changes)

These Orleans APIs exist in Quark with identical names and signatures:

| Orleans | Quark |
|---|---|
| `IGrain` | `IGrain` |
| `IGrainWithStringKey` / `IGrainWithIntegerKey` / `IGrainWithGuidKey` | identical |
| `IGrainWithIntegerCompoundKey` / `IGrainWithGuidCompoundKey` | identical |
| `IGrainFactory` | `IGrainFactory` |
| `IClusterClient` | `IClusterClient` |
| `IGrainObserver` | `IGrainObserver` |
| `[GenerateSerializer]` / `[Id(uint)]` / `[Alias]` | identical |
| `[Reentrant]` | identical |
| `[RandomPlacement]` / `[PreferLocalPlacement]` / `[HashBasedPlacement]` / `[LocalPlacement]` / `[StatelessWorker]` | identical |
| `[PersistentState("name","provider")]` / `IPersistentState<T>` | identical |
| `IRemindable` / `IGrainReminder` / `TickStatus` | identical |
| `IAsyncStream<T>` / `IAsyncObserver<T>` / `StreamId` / `StreamSequenceToken` | identical |
| `[ImplicitStreamSubscription]` | identical |
| `ITransactionalState<T>` / `[TransactionalState]` / `[Transaction]` / `TransactionOption` | identical |
| `JournaledGrain<TState,TEvent>` | identical (base class, not interface) |
| `GrainTimerCreationOptions` / `IGrainTimer` / `RegisterGrainTimer` | identical |
| `RegisterOrUpdateReminderAsync` / `UnregisterReminderAsync` / `GetRemindersAsync` | identical |
| `GetPrimaryKeyString()` / `GetPrimaryKey()` / `GetPrimaryKeyLong()` | identical (via `GrainExtensions`) |
| `AsReference<T>()` / `CreateObjectReference<T>()` / `DeleteObjectReference<T>()` | identical |
| `ILocalSiloDetails` | identical |
| `AddActivityPropagation()` | identical |

## Minor-change (small DI wiring differences)

### Host builder

```csharp
// Orleans
builder.UseOrleans(silo => { ... });
builder.UseOrleansClient(client => { ... });

// Quark
builder.UseQuark(silo => { ... });
builder.UseQuarkClient(client => { ... });
```

### Grain class → grain behavior

```csharp
// Orleans
public class CounterGrain : Grain, ICounterGrain
{
    private int _count;
    public Task IncrementAsync() { _count++; return Task.CompletedTask; }
    public Task<int> GetAsync()  => Task.FromResult(_count);
}

// Quark — behavior pattern, no base class
public sealed class CounterState { public int Count { get; set; } }

public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
{
    private readonly IActivationMemory<CounterState> _memory;
    public CounterBehavior(IActivationMemory<CounterState> memory) => _memory = memory;
    public Task IncrementAsync() { _memory.Value.Count++; return Task.CompletedTask; }
    public Task<int> GetAsync()  => Task.FromResult(_memory.Value.Count);
}
```

### Grain storage DI

```csharp
// Orleans
siloBuilder.AddMemoryGrainStorage("profileStore");
siloBuilder.AddRedisGrainStorage("profileStore");

// Quark
services.AddInMemoryGrainStorage("profileStore");
services.AddRedisGrainStorage("profileStore", opts => opts.ConnectionString = "...");
```

### Stream providers

```csharp
// Orleans
siloBuilder.AddMemoryStreams("events");

// Quark
services.AddMemoryStreams("events");
services.AddStreamableCodec<MyMsg, MyMsgCodec>(); // explicit codec registration
```

### Accessing `IGrainContext`

```csharp
// Orleans — Grain base class exposes helpers directly
this.GrainFactory
this.GrainReference
this.RegisterGrainTimer(...)

// Quark — inject IGrainContext or ICallContext into the behavior constructor
public MyBehavior(IGrainContext ctx, IGrainFactory factory, ICallContext callCtx) { ... }
```

### Reminder service

```csharp
// Orleans — called via this.RegisterOrUpdateReminderAsync(...)
await this.RegisterOrUpdateReminderAsync("reminder", dueTime, period);

// Quark — call through injected IGrainContext.ReminderService
await _ctx.ReminderService.RegisterOrUpdateReminderAsync(_ctx.GrainId, "reminder", dueTime, period);
```

## Quark-native (no Orleans equivalent)

### Engine model concepts

| Concept | Description |
|---|---|
| `IActivationMemory<TState>` | Shell-owned in-memory state injected per call via DI scope |
| `IPersistentActivationMemory<TState>` | Like `IActivationMemory<T>` with load/save hooks |
| `IGrainBehavior` | Marker interface for per-call POCO behaviors |
| `IActivationLifecycle` | `OnActivateAsync` / `OnDeactivateAsync` hooks on the behavior |
| `BehaviorStartupValidator` | Validates all DI registrations at silo startup |
| `BehaviorRegistrationGenerator` | Source generator that emits all DI wiring from behavior classes |

### TCP gateway client

```csharp
// Quark-only — standalone remote client
.UseQuarkClient(client =>
{
    client.UseLocalhostGateway(30001);
    client.AddTcpClientStreams("events"); // receive stream pushes over TCP
})
```

### `[GrainBehavior("typeName")]`

Optional attribute on a behavior class to explicitly set the `GrainType` name used by the `BehaviorRegistrationGenerator`:

```csharp
[GrainBehavior("counter-v2")]
public sealed class CounterBehavior : IGrainBehavior, ICounterGrain { ... }
```

## Migration checklist

- [ ] Replace `UseOrleans` / `UseOrleansClient` with `UseQuark` / `UseQuarkClient`
- [ ] Convert `Grain` subclasses to `IGrainBehavior` + `IActivationMemory<TState>`
- [ ] Move per-grain field state into `TState` classes, inject via `IActivationMemory<TState>`
- [ ] Replace `this.RegisterGrainTimer(...)` with `_ctx.RegisterGrainTimer(...)`
- [ ] Replace `this.RegisterOrUpdateReminderAsync(...)` with `_ctx.ReminderService.RegisterOrUpdateReminderAsync(...)`
- [ ] Register storage providers with explicit `AddInMemoryGrainStorage()` / `AddRedisGrainStorage()`
- [ ] Register stream codecs with `AddStreamableCodec<T, TCodec>()`
- [ ] Add `BehaviorRegistrationGenerator` to replace manual per-grain DI wiring
- [ ] Run AOT analyzers and fix `QRK000x` warnings
- [ ] Run Native AOT smoke build to catch trim warnings early
