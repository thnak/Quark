# Phase 3 — Grain Reminders and JournaledGrain

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement F-06 (durable grain reminders) and F-09 (`JournaledGrain<TState,TEvent>` event sourcing). Both depend only on the persistence layer from Phase 2.

**Architecture:** F-06 introduces an `IReminderService` interface with an in-memory implementation. `IRemindable` grains register reminders via `Grain` base methods; the runtime service ticks reminders and calls back through the invoker. F-09 adds `JournaledGrain<TState,TEvent>` as an abstract base that buffers events, applies them in-memory, and flushes to an `ILogStorage` on `ConfirmEventsAsync`.

**Tech Stack:** .NET 10, xUnit, `System.Threading.Timer`, `Quark.Persistence.Abstractions`

---

## File Map

| Action | File |
|---|---|
| Create | `src/Quark.Core.Abstractions/Reminders/IRemindable.cs` |
| Create | `src/Quark.Core.Abstractions/Reminders/IGrainReminder.cs` |
| Create | `src/Quark.Core.Abstractions/Reminders/TickStatus.cs` |
| Create | `src/Quark.Core.Abstractions/Grains/Grain.Reminders.cs` (partial) |
| Create | `src/Quark.Runtime/Reminders/IReminderService.cs` |
| Create | `src/Quark.Runtime/Reminders/InMemoryReminderService.cs` |
| Create | `src/Quark.Runtime/Reminders/ReminderEntry.cs` |
| Modify | `src/Quark.Runtime/SiloHostedService.cs` |
| Modify | `src/Quark.Core/QuarkServiceCollectionExtensions.cs` |
| Create | `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs` |
| Create | `src/Quark.Persistence.Abstractions/Journaling/ILogStorage.cs` |
| Create | `src/Quark.Persistence.Abstractions/Journaling/LogEntry.cs` |
| Create | `src/Quark.Persistence.InMemory/InMemoryLogStorage.cs` |
| Create | `tests/Quark.Tests.Unit/Reminders/ReminderTests.cs` |
| Create | `tests/Quark.Tests.Unit/Journaling/JournaledGrainTests.cs` |

---

## Task 9: F-06 — Grain Reminders

- [ ] **Step 9.1 — Create `IRemindable.cs`**

Create `src/Quark.Core.Abstractions/Reminders/IRemindable.cs`:

```csharp
namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Implemented by grains that receive reminder callbacks.
///     Drop-in equivalent of Orleans' <c>IRemindable</c>.
/// </summary>
public interface IRemindable
{
    /// <summary>Called by the runtime when a registered reminder fires.</summary>
    Task ReceiveReminder(string reminderName, TickStatus status);
}
```

- [ ] **Step 9.2 — Create `TickStatus.cs`**

Create `src/Quark.Core.Abstractions/Reminders/TickStatus.cs`:

```csharp
namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Carries context about the current reminder tick.
///     Drop-in equivalent of Orleans' <c>TickStatus</c>.
/// </summary>
public readonly struct TickStatus
{
    public TickStatus(DateTimeOffset firstTickTime, TimeSpan period, DateTimeOffset currentTickTime)
    {
        FirstTickTime = firstTickTime;
        Period = period;
        CurrentTickTime = currentTickTime;
    }

    /// <summary>The time the reminder first fired.</summary>
    public DateTimeOffset FirstTickTime { get; }

    /// <summary>The period between ticks.</summary>
    public TimeSpan Period { get; }

    /// <summary>The time this tick fired.</summary>
    public DateTimeOffset CurrentTickTime { get; }
}
```

- [ ] **Step 9.3 — Create `IGrainReminder.cs`**

Create `src/Quark.Core.Abstractions/Reminders/IGrainReminder.cs`:

```csharp
namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Handle for a registered grain reminder.
///     Drop-in equivalent of Orleans' <c>IGrainReminder</c>.
/// </summary>
public interface IGrainReminder
{
    /// <summary>The reminder's logical name.</summary>
    string ReminderName { get; }
}
```

- [ ] **Step 9.4 — Create `IReminderService.cs`**

Create `src/Quark.Runtime/Reminders/IReminderService.cs`:

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;

namespace Quark.Runtime.Reminders;

internal interface IReminderService
{
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period);

    Task UnregisterReminderAsync(GrainId grainId, IGrainReminder reminder);

    Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(GrainId grainId);
}
```

- [ ] **Step 9.5 — Create `ReminderEntry.cs`**

Create `src/Quark.Runtime/Reminders/ReminderEntry.cs`:

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;

namespace Quark.Runtime.Reminders;

internal sealed class ReminderEntry : IGrainReminder, IDisposable
{
    private readonly Timer _timer;

    public ReminderEntry(
        GrainId grainId,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        Func<GrainId, string, TickStatus, Task> onFire)
    {
        GrainId = grainId;
        ReminderName = reminderName;
        Period = period;
        FirstTickTime = DateTimeOffset.UtcNow + dueTime;
        _timer = new Timer(
            async _ => await onFire(grainId, reminderName,
                new TickStatus(FirstTickTime, period, DateTimeOffset.UtcNow)),
            null, dueTime, period);
    }

    public GrainId GrainId { get; }
    public string ReminderName { get; }
    public TimeSpan Period { get; }
    public DateTimeOffset FirstTickTime { get; }

    public void Reschedule(TimeSpan dueTime, TimeSpan period)
    {
        _timer.Change(dueTime, period);
    }

    public void Dispose() => _timer.Dispose();
}
```

- [ ] **Step 9.6 — Create `InMemoryReminderService.cs`**

Create `src/Quark.Runtime/Reminders/InMemoryReminderService.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Runtime;

namespace Quark.Runtime.Reminders;

internal sealed class InMemoryReminderService : IReminderService, IDisposable
{
    private readonly ConcurrentDictionary<(GrainId, string), ReminderEntry> _reminders = new();
    private readonly IGrainCallInvoker _invoker;

    public InMemoryReminderService(IGrainCallInvoker invoker)
    {
        _invoker = invoker;
    }

    public Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period)
    {
        var key = (grainId, reminderName);
        if (_reminders.TryGetValue(key, out ReminderEntry? existing))
        {
            existing.Reschedule(dueTime, period);
            return Task.FromResult<IGrainReminder>(existing);
        }

        var entry = new ReminderEntry(grainId, reminderName, dueTime, period, FireAsync);
        _reminders[key] = entry;
        return Task.FromResult<IGrainReminder>(entry);
    }

    public Task UnregisterReminderAsync(GrainId grainId, IGrainReminder reminder)
    {
        var key = (grainId, reminder.ReminderName);
        if (_reminders.TryRemove(key, out ReminderEntry? entry))
            entry.Dispose();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(GrainId grainId)
    {
        var results = _reminders
            .Where(kv => kv.Key.Item1.Equals(grainId))
            .Select(kv => (IGrainReminder)kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<IGrainReminder>>(results);
    }

    private async Task FireAsync(GrainId grainId, string reminderName, TickStatus status)
    {
        // Route reminder callback through the grain's invoker.
        // Method ID 0 is reserved for ReceiveReminder on IRemindable.
        await _invoker.InvokeVoidAsync(grainId, methodId: 0,
            arguments: [reminderName, status]).ConfigureAwait(false);
    }

    public void Dispose()
    {
        foreach (var entry in _reminders.Values) entry.Dispose();
        _reminders.Clear();
    }
}
```

> **Note on method ID 0:** `IRemindable.ReceiveReminder` needs a consistent method ID. The `GrainMethodInvoker` for any `IRemindable` grain must handle `methodId=0` as `ReceiveReminder`. Document this convention in CLAUDE.md.

- [ ] **Step 9.7 — Add reminder methods to `IGrainContext` and `Grain`**

In `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs`, add:

```csharp
    // Reminder methods — only valid on IRemindable grains.
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(string reminderName, TimeSpan dueTime, TimeSpan period);
    Task UnregisterReminderAsync(IGrainReminder reminder);
    Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync();
```

In `src/Quark.Core.Abstractions/Grains/Grain.cs`, add:

```csharp
    protected Task<IGrainReminder> RegisterOrUpdateReminder(string reminderName, TimeSpan dueTime, TimeSpan period)
        => GrainContext.RegisterOrUpdateReminderAsync(reminderName, dueTime, period);

    protected Task UnregisterReminder(IGrainReminder reminder)
        => GrainContext.UnregisterReminderAsync(reminder);

    protected Task<IReadOnlyList<IGrainReminder>> GetReminders()
        => GrainContext.GetRemindersAsync();
```

- [ ] **Step 9.8 — Implement reminder methods in `GrainContext`**

In `src/Quark.Runtime/GrainContext.cs`, inject `IReminderService` and implement:

```csharp
// Add constructor parameter:
// private readonly IReminderService? _reminderService;

public Task<IGrainReminder> RegisterOrUpdateReminderAsync(string reminderName, TimeSpan dueTime, TimeSpan period)
{
    if (_reminderService is null) throw new InvalidOperationException("No IReminderService registered.");
    return _reminderService.RegisterOrUpdateReminderAsync(GrainId, reminderName, dueTime, period);
}

public Task UnregisterReminderAsync(IGrainReminder reminder)
    => _reminderService?.UnregisterReminderAsync(GrainId, reminder) ?? Task.CompletedTask;

public Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync()
    => _reminderService?.GetRemindersAsync(GrainId) ?? Task.FromResult<IReadOnlyList<IGrainReminder>>([]);
```

Register `InMemoryReminderService` as `IReminderService` in `AddQuarkRuntime()`.

- [ ] **Step 9.9 — Write and run failing reminder test**

Create `tests/Quark.Tests.Unit/Reminders/ReminderTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Reminders;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Reminders;

public sealed class ReminderTests : IAsyncDisposable
{
    private readonly GrainCallFixture _fixture = new();

    [Fact]
    public async Task Reminder_FiresCallback_AfterDueTime()
    {
        IRemindableGrain grain = _fixture.Client.GetGrain<IRemindableGrain>("reminder-test");
        await grain.SetupReminderAsync(dueTime: TimeSpan.FromMilliseconds(50), period: Timeout.InfiniteTimeSpan);

        await Task.Delay(300);

        int count = await grain.GetFireCountAsync();
        Assert.True(count >= 1, $"Expected >= 1 reminder fire, got {count}");
    }

    [Fact]
    public async Task Reminder_CanBeUnregistered()
    {
        IRemindableGrain grain = _fixture.Client.GetGrain<IRemindableGrain>("reminder-cancel-test");
        await grain.SetupReminderAsync(dueTime: TimeSpan.FromMilliseconds(50), period: TimeSpan.FromMilliseconds(50));
        await Task.Delay(200);
        int countBefore = await grain.GetFireCountAsync();
        await grain.CancelReminderAsync();
        await Task.Delay(200);
        int countAfter = await grain.GetFireCountAsync();
        Assert.Equal(countBefore, countAfter);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    public interface IRemindableGrain : IGrain, IGrainWithStringKey
    {
        Task SetupReminderAsync(TimeSpan dueTime, TimeSpan period);
        Task<int> GetFireCountAsync();
        Task CancelReminderAsync();
    }

    public sealed class RemindableGrain : Grain, IRemindableGrain, IRemindable
    {
        private IGrainReminder? _reminder;
        private int _fireCount;

        public async Task SetupReminderAsync(TimeSpan dueTime, TimeSpan period)
            => _reminder = await RegisterOrUpdateReminder("tick", dueTime, period);

        public Task<int> GetFireCountAsync() => Task.FromResult(_fireCount);

        public async Task CancelReminderAsync()
        {
            if (_reminder is not null) await UnregisterReminder(_reminder);
        }

        public Task ReceiveReminder(string reminderName, TickStatus status)
        {
            Interlocked.Increment(ref _fireCount);
            return Task.CompletedTask;
        }
    }
}
```

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~ReminderTests" -v minimal
```
Expected: 2 tests PASS.

- [ ] **Step 9.10 — Run full suite and commit**

```bash
dotnet test Quark.slnx -v minimal
git add src/Quark.Core.Abstractions/Reminders/ \
        src/Quark.Runtime/Reminders/ \
        src/Quark.Core.Abstractions/Hosting/IGrainContext.cs \
        src/Quark.Core.Abstractions/Grains/Grain.cs \
        src/Quark.Runtime/GrainContext.cs \
        tests/Quark.Tests.Unit/Reminders/
git commit -m "feat(F-06): add grain reminders (IRemindable, RegisterOrUpdateReminder, InMemoryReminderService)"
```

---

## Task 10: F-09 — `JournaledGrain<TState, TEvent>`

- [ ] **Step 10.1 — Create `ILogStorage.cs`**

Create `src/Quark.Persistence.Abstractions/Journaling/ILogStorage.cs`:

```csharp
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>Append-only log storage for journaled grains.</summary>
public interface ILogStorage
{
    Task<IReadOnlyList<LogEntry>> ReadEntriesAsync(GrainId grainId, int fromVersion, int toVersion, CancellationToken ct = default);
    Task AppendEntriesAsync(GrainId grainId, int expectedVersion, IReadOnlyList<LogEntry> entries, CancellationToken ct = default);
}
```

- [ ] **Step 10.2 — Create `LogEntry.cs`**

Create `src/Quark.Persistence.Abstractions/Journaling/LogEntry.cs`:

```csharp
namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>A single versioned event record in the log.</summary>
public sealed class LogEntry
{
    public LogEntry(int version, object @event)
    {
        Version = version;
        Event = @event;
    }

    public int Version { get; }
    public object Event { get; }
}
```

- [ ] **Step 10.3 — Create `InMemoryLogStorage.cs`**

Create `src/Quark.Persistence.InMemory/InMemoryLogStorage.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;

namespace Quark.Persistence.InMemory;

public sealed class InMemoryLogStorage : ILogStorage
{
    private readonly ConcurrentDictionary<GrainId, List<LogEntry>> _logs = new();

    public Task<IReadOnlyList<LogEntry>> ReadEntriesAsync(GrainId grainId, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        if (!_logs.TryGetValue(grainId, out var log))
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);

        var slice = log.Where(e => e.Version >= fromVersion && e.Version < toVersion).ToList();
        return Task.FromResult<IReadOnlyList<LogEntry>>(slice);
    }

    public Task AppendEntriesAsync(GrainId grainId, int expectedVersion, IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
    {
        var log = _logs.GetOrAdd(grainId, _ => []);
        lock (log)
        {
            if (log.Count != expectedVersion)
                throw new InvalidOperationException($"Version conflict: expected {expectedVersion}, found {log.Count}.");
            log.AddRange(entries);
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 10.4 — Create `JournaledGrain.cs`**

Create `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Abstract base class for event-sourced grains.
///     <typeparamref name="TState" /> must implement <c>Apply(TEvent)</c> as a public method.
///     Drop-in equivalent of Orleans' <c>JournaledGrain&lt;TState, TEvent&gt;</c>.
/// </summary>
public abstract class JournaledGrain<TState, TEvent> : Grain
    where TState : class, new()
{
    private readonly List<TEvent> _stagedEvents = [];
    private ILogStorage? _logStorage;
    private int _confirmedVersion;

    /// <summary>The number of confirmed (persisted) events.</summary>
    protected int Version => _confirmedVersion;

    /// <summary>The current in-memory state (includes staged but not yet confirmed events).</summary>
    protected TState State { get; private set; } = new();

    /// <summary>
    ///     Injects the log storage. Called by the activator factory.
    ///     Use <see cref="InjectLogStorage" /> from a hand-written factory, or rely on the
    ///     code generator to call this in generated activators.
    /// </summary>
    protected void InjectLogStorage(ILogStorage logStorage) => _logStorage = logStorage;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (_logStorage is not null)
            await ReloadFromLogAsync(cancellationToken);
    }

    /// <summary>Stages an event to be applied in memory. Call <see cref="ConfirmEventsAsync" /> to persist.</summary>
    protected void RaiseEvent(TEvent @event)
    {
        _stagedEvents.Add(@event);
        ApplyEvent(@event);
    }

    /// <summary>Stages multiple events.</summary>
    protected void RaiseEvents(IEnumerable<TEvent> events)
    {
        foreach (TEvent e in events) RaiseEvent(e);
    }

    /// <summary>Persists all staged events to the log storage.</summary>
    protected async Task ConfirmEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_stagedEvents.Count == 0) return;
        if (_logStorage is null) throw new InvalidOperationException("No ILogStorage injected.");

        var entries = _stagedEvents
            .Select((e, i) => new LogEntry(_confirmedVersion + i, e!))
            .ToList();

        await _logStorage.AppendEntriesAsync(GrainId, _confirmedVersion, entries, cancellationToken);
        _confirmedVersion += _stagedEvents.Count;
        _stagedEvents.Clear();
    }

    /// <summary>Retrieves confirmed events in the range [<paramref name="fromVersion"/>, <paramref name="toVersion"/>).</summary>
    protected async Task<IReadOnlyList<TEvent>> RetrieveConfirmedEvents(
        int fromVersion, int toVersion, CancellationToken cancellationToken = default)
    {
        if (_logStorage is null) return [];
        IReadOnlyList<LogEntry> entries = await _logStorage.ReadEntriesAsync(
            GrainId, fromVersion, toVersion, cancellationToken);
        return entries.Select(e => (TEvent)e.Event).ToList();
    }

    private async Task ReloadFromLogAsync(CancellationToken ct)
    {
        IReadOnlyList<LogEntry> all = await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct);
        foreach (LogEntry entry in all)
        {
            ApplyEvent((TEvent)entry.Event);
            _confirmedVersion = entry.Version + 1;
        }
    }

    private void ApplyEvent(TEvent @event)
    {
        // Convention: TState must expose a public void Apply(TEvent) method.
        var method = typeof(TState).GetMethod("Apply", [typeof(TEvent)]);
        if (method is null)
            throw new InvalidOperationException(
                $"{typeof(TState).Name} must have a public void Apply({typeof(TEvent).Name}) method.");
        method.Invoke(State, [@event]);
    }
}
```

> **AOT note:** The `Apply` call above uses reflection as a convenience for the initial implementation. For AOT-safe production use, the code generator should detect `JournaledGrain<TState,TEvent>` subclasses and emit a static `Apply` dispatch. Mark with `[RequiresUnreferencedCode]` for now and file a follow-up to code-gen the dispatch.

- [ ] **Step 10.5 — Write and run failing test**

Create `tests/Quark.Tests.Unit/Journaling/JournaledGrainTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class JournaledGrainTests
{
    private static async Task<(TodoGrain, GrainContext)> ActivateAsync()
    {
        var storage = new InMemoryLogStorage();
        var grain = new TodoGrain(storage);
        var id = new GrainId(new GrainType("TodoGrain"), "1");
        var ctx = new GrainContext(id, new NullGrainFactory(), new NullServiceProvider());
        var activation = new GrainActivation(grain, ctx, NullLogger<GrainActivation>.Instance);
        await ctx.ActivateAsync(grain);
        return (grain, ctx);
    }

    [Fact]
    public async Task RaiseEvent_UpdatesInMemoryState()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("buy milk");
        Assert.Contains("buy milk", grain.State.Items);
    }

    [Fact]
    public async Task ConfirmEvents_PersistsToLog()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("task A");
        await grain.SaveAsync();
        Assert.Equal(1, grain.Version);
    }

    [Fact]
    public async Task RetrieveConfirmedEvents_ReturnsHistory()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("A");
        grain.Add("B");
        await grain.SaveAsync();

        var history = await grain.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("A", ((ItemAdded)history[0]).Item);
        Assert.Equal("B", ((ItemAdded)history[1]).Item);
    }

    // --- test grain ---

    public sealed class TodoState
    {
        public List<string> Items { get; } = [];
        public void Apply(TodoEvent e)
        {
            if (e is ItemAdded added) Items.Add(added.Item);
            else if (e is ItemRemoved removed) Items.Remove(removed.Item);
        }
    }

    public abstract record TodoEvent;
    public sealed record ItemAdded(string Item) : TodoEvent;
    public sealed record ItemRemoved(string Item) : TodoEvent;

    public sealed class TodoGrain : JournaledGrain<TodoState, TodoEvent>
    {
        public TodoGrain(InMemoryLogStorage storage) => InjectLogStorage(storage);
        public new TodoState State => base.State;
        public new int Version => base.Version;
        public void Add(string item) => RaiseEvent(new ItemAdded(item));
        public Task SaveAsync() => ConfirmEventsAsync();
        public Task<IReadOnlyList<TodoEvent>> GetHistoryAsync() => RetrieveConfirmedEvents(0, Version);
    }
}
```

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~JournaledGrainTests" -v minimal
```
Expected: 3 tests PASS.

- [ ] **Step 10.6 — Run full suite and commit**

```bash
dotnet test Quark.slnx -v minimal
git add src/Quark.Persistence.Abstractions/Journaling/ \
        src/Quark.Persistence.InMemory/InMemoryLogStorage.cs \
        tests/Quark.Tests.Unit/Journaling/
git commit -m "feat(F-09): add JournaledGrain<TState,TEvent> with ILogStorage and InMemoryLogStorage"
```

---

## Task 11: Tick FEATURES.md

- [ ] Mark F-06 and F-09 complete in `FEATURES.md` and commit.

```bash
git add FEATURES.md
git commit -m "docs: mark Phase 3 features complete in FEATURES.md"
```
