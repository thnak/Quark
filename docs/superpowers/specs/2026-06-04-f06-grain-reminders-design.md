# F-06 Grain Reminders — Design Spec

**Date:** 2026-06-04
**Status:** Approved
**Phase:** 3 (first feature)
**Complexity:** L
**Dependencies:** F-03 (conceptual; no hard code dependency)

---

## Background

Grain reminders are durable, period-firing callbacks that survive silo restarts. Unlike grain timers (F-03), which are in-memory and die with the grain activation, reminders are persisted to a storage backend and reloaded on silo startup. Quark must implement the full Orleans `IRemindable` contract so applications like scheduled jobs, heartbeat grains, and time-driven workflows can port without modification.

---

## Decisions

| Question | Decision |
|---|---|
| Tackle F-06 and F-09 together? | One at a time — F-06 first |
| Storage abstraction | Dedicated `IReminderStorage` (not reusing `IGrainStorage`) |
| Where do grain-facing abstractions live? | New `Quark.Reminders.Abstractions` package for storage/service impl; grain-facing types (`IRemindable`, `IGrainReminder`, `TickStatus`, `IReminderService` interface) in `Quark.Core.Abstractions` |
| How do grains access `IReminderService`? | Gateway on `IGrainContext.ReminderService` (Approach B) — context resolves lazily from `ServiceProvider` |
| Polling mechanism | Fixed `PeriodicTimer` interval (default 1 s), configurable via `ReminderOptions` |
| Storage backends | In-memory (`Quark.Reminders.InMemory`) + Redis (`Quark.Reminders.Redis`) |

---

## Package Layout

```
Quark.Core.Abstractions/Reminders/
    IRemindable.cs
    IGrainReminder.cs
    TickStatus.cs
    IReminderService.cs
    ReminderMethodIds.cs        ← internal; well-known method ID for code-gen

Quark.Reminders.Abstractions    ← NEW
    IReminderStorage.cs
    ReminderEntry.cs
    DefaultReminderService.cs   ← concrete polling impl, no platform deps

Quark.Reminders.InMemory        ← NEW
    InMemoryReminderStorage.cs
    InMemoryReminderServiceCollectionExtensions.cs

Quark.Reminders.Redis           ← NEW
    RedisReminderStorage.cs
    RedisReminderServiceCollectionExtensions.cs
```

**Dependency graph (no cycles):**

```
Quark.Core.Abstractions
    ↑
Quark.Reminders.Abstractions   (+ Quark.Core.Abstractions for GrainId, IGrainCallInvoker)
    ↑                ↑
Quark.Reminders.InMemory    Quark.Reminders.Redis
```

`Quark.Runtime` and `SiloHostedService` require **no changes** — `DefaultReminderService` registers itself as `IHostedService` and the host starts/stops it automatically.

---

## API Surface

### `Quark.Core.Abstractions/Reminders/`

```csharp
public interface IRemindable
{
    Task ReceiveReminder(string reminderName, TickStatus status);
}

public readonly record struct TickStatus(
    DateTimeOffset FirstTickTime,
    TimeSpan Period,
    DateTimeOffset CurrentTickTime);

public interface IGrainReminder
{
    string ReminderName { get; }
    bool IsValid { get; }
}

public interface IReminderService
{
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        CancellationToken ct = default);

    Task UnregisterReminderAsync(
        GrainId grainId, string name,
        CancellationToken ct = default);

    Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(
        GrainId grainId,
        CancellationToken ct = default);
}

// public — must be visible to Quark.Reminders.Abstractions (DefaultReminderService) and Quark.CodeGenerator
public static class ReminderMethodIds
{
    public const uint ReceiveReminder = 0xFFFF_FF00u;
}
```

### `IGrainContext` addition

```csharp
IReminderService? ReminderService { get; }
```

`GrainContext` implements it as:
```csharp
public IReminderService? ReminderService => ServiceProvider.GetService<IReminderService>();
```

### `Grain` base additions

```csharp
protected Task<IGrainReminder> RegisterOrUpdateReminderAsync(
    string reminderName, TimeSpan dueTime, TimeSpan period)
    => (GrainContext.ReminderService
        ?? throw new InvalidOperationException(
            "No IReminderService is registered. Call AddInMemoryReminders() or AddRedisReminders()."))
        .RegisterOrUpdateReminderAsync(GrainId, reminderName, dueTime, period);

protected Task UnregisterReminderAsync(IGrainReminder reminder)
    => (GrainContext.ReminderService
        ?? throw new InvalidOperationException("No IReminderService is registered."))
        .UnregisterReminderAsync(GrainId, reminder.ReminderName);

protected Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync()
    => (GrainContext.ReminderService
        ?? throw new InvalidOperationException("No IReminderService is registered."))
        .GetRemindersAsync(GrainId);
```

---

## Storage Model (`Quark.Reminders.Abstractions`)

```csharp
public sealed class ReminderEntry
{
    public required GrainId GrainId { get; init; }
    public required string ReminderName { get; init; }
    public required DateTimeOffset StartAt { get; init; }    // wall-clock when first registered
    public required TimeSpan Period { get; init; }
    public required DateTimeOffset NextFireAt { get; init; } // pre-computed; updated after each tick
}

public interface IReminderStorage
{
    Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default);
    Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default);
    Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default);
}
```

Storage key convention: `"{grainType}|{grainKey}|{reminderName}"` — used by both in-memory and Redis implementations.

`NextFireAt` is persisted so the polling loop performs a single `<= DateTimeOffset.UtcNow` comparison per entry. After firing, the service writes `NextFireAt += Period` to storage *before* invoking `ReceiveReminder`. This provides at-least-once delivery semantics on crash: on restart the entry is due immediately and fires once more.

---

## `DefaultReminderService`

Lives in `Quark.Reminders.Abstractions`. Depends only on `IReminderStorage` + `IGrainCallInvoker` (both from `Quark.Core.Abstractions` / `Quark.Reminders.Abstractions`).

```csharp
public sealed class DefaultReminderService : IReminderService, IHostedService, IAsyncDisposable
{
    private readonly IReminderStorage _storage;
    private readonly IGrainCallInvoker _invoker;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // IHostedService
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_loopTask is not null) await _loopTask.ConfigureAwait(false);
    }

    // Polling loop
    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            IReadOnlyList<ReminderEntry> all = await _storage.ReadAllAsync(ct).ConfigureAwait(false);
            foreach (ReminderEntry entry in all.Where(e => e.NextFireAt <= DateTimeOffset.UtcNow))
            {
                // Advance before firing — at-least-once on crash
                ReminderEntry updated = entry with { NextFireAt = entry.NextFireAt + entry.Period };
                await _storage.UpsertAsync(updated, ct).ConfigureAwait(false);

                var status = new TickStatus(entry.StartAt, entry.Period, entry.NextFireAt);
                await _invoker.InvokeVoidAsync(
                    entry.GrainId,
                    ReminderMethodIds.ReceiveReminder,
                    [entry.ReminderName, status],
                    ct).ConfigureAwait(false);
            }
        }
    }
}
```

**`ReminderOptions`** (one knob):
```csharp
public sealed class ReminderOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
```

---

## Storage Implementations

### `InMemoryReminderStorage`

`ConcurrentDictionary<string, ReminderEntry>` keyed on the storage key convention above. All operations are synchronous (`Task.CompletedTask`). Deep-copy on read/write is not required since `ReminderEntry` is an immutable record.

### `RedisReminderStorage`

Uses a single Redis Hash (`HSET` / `HGET` / `HDEL` / `HGETALL`) at key `"quark:reminders"`. Entries serialized with `System.Text.Json` (not the Quark serializer — `ReminderEntry` is an infrastructure DTO, not a grain message). `GrainId` serialized as `"{type}|{key}"`.

---

## DI Registration

```csharp
// Quark.Reminders.InMemory — for tests and development
services.AddInMemoryReminders();
services.AddInMemoryReminders(opts => opts.PollInterval = TimeSpan.FromMilliseconds(100));

// Quark.Reminders.Redis — for production
services.AddRedisReminders(opts => opts.ConnectionString = "localhost:6379");
```

Both helpers register:
- `IReminderStorage` as a singleton (in-memory or Redis variant)
- `DefaultReminderService` as `IReminderService` (singleton)
- Same `DefaultReminderService` instance as `IHostedService`
- `IOptions<ReminderOptions>` configured from the `Action<ReminderOptions>` callback

---

## Code Generator Changes

**`GrainProxyGenerator.cs`** — when emitting the `Invoke` switch for a grain class, check if the grain's interface list contains `"Quark.Core.Abstractions.Reminders.IRemindable"` (by fully-qualified name, no runtime reference required). If so, emit:

```csharp
0xFFFF_FF00u => InvokeReceiveReminderAsync(grain, args),

private static async ValueTask<object?> InvokeReceiveReminderAsync(Grain grain, object?[]? args)
{
    await ((global::Quark.Core.Abstractions.Reminders.IRemindable)grain)
        .ReceiveReminder((string)args![0]!, (global::Quark.Core.Abstractions.Reminders.TickStatus)args[1]!);
    return null;
}
```

Grains that do not implement `IRemindable` are unaffected. Hand-written test invokers add the `0xFFFF_FF00u` case manually.

**`GrainActivatorGenerator.cs`** — no changes.

---

## Tests

### `Quark.Tests.Unit/Reminders/ReminderServiceTests.cs`

| Test | Assertion |
|---|---|
| `Reminder_FiresAfterDueTime` | `ReceiveReminder` called once after `dueTime` elapses |
| `Reminder_FiresRepeatedly` | `ReceiveReminder` called ≥3 times over 3× `period` |
| `UnregisterReminder_StopsFutureFirings` | no more calls after `UnregisterReminderAsync` |
| `Reminder_RegisterOrUpdate_UpdatesPeriod` | second `RegisterOrUpdateReminderAsync` with new period replaces existing |

Uses `FakeReminderStorage` (in-memory dict) and `FakeGrainCallInvoker` (records invocations).

### `Quark.Tests.Integration/ReminderIntegrationTests.cs`

| Test | Assertion |
|---|---|
| `Reminder_FiresOnIRemindableGrain` | full `TestCluster` round-trip; grain receives `ReceiveReminder` |
| `Reminder_SurvivesSimulatedRestart` | write reminder to storage, recreate `DefaultReminderService`, verify it fires |
| `GetReminders_ReturnsRegisteredReminders` | `GetRemindersAsync()` returns all registered reminders for a grain |

### `Quark.Tests.CodeGenerator/ReminderInvokerGeneratorTests.cs`

| Test | Assertion |
|---|---|
| `Generator_EmitsReceiveReminderCase_ForIRemindableGrain` | generated invoker contains `0xFFFF_FF00u` case |
| `Generator_DoesNotEmitReceiveReminderCase_ForPlainGrain` | non-`IRemindable` grain invoker has no `0xFFFF_FF00u` case |

---

## Sequence: Reminder Fires

```
PeriodicTimer tick
  → DefaultReminderService reads IReminderStorage.ReadAllAsync()
  → filters entries where NextFireAt <= UtcNow
  → IReminderStorage.UpsertAsync(entry with NextFireAt += Period)
  → IGrainCallInvoker.InvokeVoidAsync(grainId, 0xFFFF_FF00u, [name, tickStatus])
      → LocalGrainCallInvoker activates grain if needed
      → GrainActivation posts to grain scheduler channel
      → Generated invoker: 0xFFFF_FF00u → IRemindable.ReceiveReminder(name, status)
```

---

## Orleans Compatibility

| Orleans API | Quark equivalent |
|---|---|
| `this.RegisterOrUpdateReminder(name, dueTime, period)` | `Grain.RegisterOrUpdateReminderAsync(name, dueTime, period)` |
| `this.UnregisterReminder(handle)` | `Grain.UnregisterReminderAsync(handle)` |
| `this.GetReminders()` | `Grain.GetRemindersAsync()` |
| `IRemindable.ReceiveReminder(name, status)` | identical |
| `TickStatus` | identical |
| `siloBuilder.UseInMemoryReminderService()` | `services.AddInMemoryReminders()` |
