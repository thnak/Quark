# F-06 Grain Reminders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement durable grain reminders — `IRemindable`, `RegisterOrUpdateReminderAsync`, `IReminderStorage`, and a polling `DefaultReminderService` — compatible with the Orleans reminder API.

**Architecture:** Grain-facing types (`IRemindable`, `IGrainReminder`, `TickStatus`, `IReminderService` interface, `ReminderMethodIds`) live in `Quark.Core.Abstractions/Reminders/`. The `IGrainContext` gateway exposes `IReminderService?` so `Grain` base methods delegate through context. A new `Quark.Reminders.Abstractions` package holds `IReminderStorage`, `ReminderEntry`, `DefaultReminderService` (the polling `IHostedService`). `Quark.Reminders.InMemory` and `Quark.Reminders.Redis` provide concrete storage backends. `LocalGrainCallInvoker` dispatches `ReceiveReminder` natively — no code generator changes required.

**Tech Stack:** .NET 10, `System.Threading.PeriodicTimer`, `System.Text.Json` (Redis serialisation), `StackExchange.Redis`, `Microsoft.Extensions.Hosting.Abstractions`, xUnit.

---

## File Map

**New files — `Quark.Core.Abstractions`:**
- `src/Quark.Core.Abstractions/Reminders/IRemindable.cs`
- `src/Quark.Core.Abstractions/Reminders/IGrainReminder.cs`
- `src/Quark.Core.Abstractions/Reminders/TickStatus.cs`
- `src/Quark.Core.Abstractions/Reminders/IReminderService.cs`
- `src/Quark.Core.Abstractions/Reminders/ReminderMethodIds.cs`

**Modified files — `Quark.Core.Abstractions` + `Quark.Runtime`:**
- `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs` — add `IReminderService? ReminderService { get; }`
- `src/Quark.Runtime/GrainContext.cs` — implement `ReminderService`
- `src/Quark.Core.Abstractions/Grains/Grain.cs` — add 3 protected reminder methods
- `src/Quark.Runtime/LocalGrainCallInvoker.cs` — native `IRemindable` dispatch

**New package — `Quark.Reminders.Abstractions`:**
- `src/Quark.Reminders.Abstractions/Quark.Reminders.Abstractions.csproj`
- `src/Quark.Reminders.Abstractions/IReminderStorage.cs`
- `src/Quark.Reminders.Abstractions/ReminderEntry.cs`
- `src/Quark.Reminders.Abstractions/ReminderOptions.cs`
- `src/Quark.Reminders.Abstractions/GrainReminder.cs`
- `src/Quark.Reminders.Abstractions/DefaultReminderService.cs`

**New package — `Quark.Reminders.InMemory`:**
- `src/Quark.Reminders.InMemory/Quark.Reminders.InMemory.csproj`
- `src/Quark.Reminders.InMemory/InMemoryReminderStorage.cs`
- `src/Quark.Reminders.InMemory/InMemoryReminderServiceCollectionExtensions.cs`

**New package — `Quark.Reminders.Redis`:**
- `src/Quark.Reminders.Redis/Quark.Reminders.Redis.csproj`
- `src/Quark.Reminders.Redis/RedisReminderStorage.cs`
- `src/Quark.Reminders.Redis/RedisReminderServiceCollectionExtensions.cs`

**Test files:**
- `tests/Quark.Tests.Unit/Reminders/ReminderServiceTests.cs`
- `tests/Quark.Tests.Integration/ReminderIntegrationTests.cs`
- `tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj` — add project ref
- `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj` — add project ref

**Housekeeping:**
- `Quark.slnx` — register 3 new projects
- `FEATURES.md` — check off F-06

---

## Task 1: Core reminder types + `IGrainContext` gateway

**Files:**
- Create: `src/Quark.Core.Abstractions/Reminders/IRemindable.cs`
- Create: `src/Quark.Core.Abstractions/Reminders/IGrainReminder.cs`
- Create: `src/Quark.Core.Abstractions/Reminders/TickStatus.cs`
- Create: `src/Quark.Core.Abstractions/Reminders/IReminderService.cs`
- Create: `src/Quark.Core.Abstractions/Reminders/ReminderMethodIds.cs`
- Modify: `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs`
- Modify: `src/Quark.Runtime/GrainContext.cs`

- [ ] **Step 1: Create `IRemindable.cs`**

```csharp
// src/Quark.Core.Abstractions/Reminders/IRemindable.cs
namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Implemented by grains that receive durable reminder callbacks.
///     Orleans-compatible drop-in.
/// </summary>
public interface IRemindable
{
    Task ReceiveReminder(string reminderName, TickStatus status);
}
```

- [ ] **Step 2: Create `TickStatus.cs`**

```csharp
// src/Quark.Core.Abstractions/Reminders/TickStatus.cs
namespace Quark.Core.Abstractions.Reminders;

/// <summary>Status snapshot passed to <see cref="IRemindable.ReceiveReminder" />.</summary>
public readonly record struct TickStatus(
    DateTimeOffset FirstTickTime,
    TimeSpan Period,
    DateTimeOffset CurrentTickTime);
```

- [ ] **Step 3: Create `IGrainReminder.cs`**

```csharp
// src/Quark.Core.Abstractions/Reminders/IGrainReminder.cs
namespace Quark.Core.Abstractions.Reminders;

/// <summary>Handle returned by <c>RegisterOrUpdateReminderAsync</c>.</summary>
public interface IGrainReminder
{
    string ReminderName { get; }
    bool IsValid { get; }
}
```

- [ ] **Step 4: Create `IReminderService.cs`**

```csharp
// src/Quark.Core.Abstractions/Reminders/IReminderService.cs
using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Runtime service that manages durable reminder scheduling.
///     Exposed via <see cref="Hosting.IGrainContext.ReminderService" />.
/// </summary>
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
```

- [ ] **Step 5: Create `ReminderMethodIds.cs`**

```csharp
// src/Quark.Core.Abstractions/Reminders/ReminderMethodIds.cs
namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Well-known reserved method ID for <see cref="IRemindable.ReceiveReminder" />.
///     <see cref="Quark.Runtime.LocalGrainCallInvoker" /> dispatches this ID
///     directly without going through the grain's <c>IGrainMethodInvoker</c>.
/// </summary>
public static class ReminderMethodIds
{
    public const uint ReceiveReminder = 0xFFFF_FF00u;
}
```

- [ ] **Step 6: Add `ReminderService` to `IGrainContext`**

In `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs`, add the property after the `RegisterTimer` method:

```csharp
using Quark.Core.Abstractions.Reminders;
// (add this using at the top)

    /// <summary>
    ///     The reminder service for this activation, or <c>null</c> if no reminder provider is registered.
    ///     Use <see cref="Grain.RegisterOrUpdateReminderAsync" /> rather than calling this directly.
    /// </summary>
    IReminderService? ReminderService { get; }
```

Full updated file after edit:
```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Lifecycle;
using Quark.Core.Abstractions.Reminders;
using Quark.Core.Abstractions.Timers;

namespace Quark.Core.Abstractions.Hosting;

public interface IGrainContext
{
    GrainId GrainId { get; }
    ILifecycleSubject ObservableLifecycle { get; }
    GrainActivationStatus ActivationStatus { get; }
    IGrainFactory GrainFactory { get; }
    IServiceProvider ServiceProvider { get; }
    void Deactivate(DeactivationReason reason);
    IGrainTimer RegisterTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options);
    IReminderService? ReminderService { get; }
}
```

- [ ] **Step 7: Implement `ReminderService` in `GrainContext`**

In `src/Quark.Runtime/GrainContext.cs`, add the implementation. After the `RegisterTimer` method, add:

```csharp
    /// <inheritdoc />
    public IReminderService? ReminderService =>
        ServiceProvider.GetService(typeof(IReminderService)) as IReminderService;
```

Also add the using at the top of the file:
```csharp
using Quark.Core.Abstractions.Reminders;
```

- [ ] **Step 8: Build to confirm no errors**

```bash
dotnet build src/Quark.Core.Abstractions/Quark.Core.Abstractions.csproj
dotnet build src/Quark.Runtime/Quark.Runtime.csproj
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/Quark.Core.Abstractions/Reminders/ \
        src/Quark.Core.Abstractions/Hosting/IGrainContext.cs \
        src/Quark.Runtime/GrainContext.cs
git commit -m "feat(F-06): add IRemindable, IGrainReminder, TickStatus, IReminderService to Core.Abstractions; wire IGrainContext gateway"
```

---

## Task 2: `Grain` base reminder methods + `LocalGrainCallInvoker` native dispatch

**Files:**
- Modify: `src/Quark.Core.Abstractions/Grains/Grain.cs`
- Modify: `src/Quark.Runtime/LocalGrainCallInvoker.cs`

- [ ] **Step 1: Add protected reminder methods to `Grain`**

In `src/Quark.Core.Abstractions/Grains/Grain.cs`, add these three methods after `RegisterGrainTimer`. Also add `using Quark.Core.Abstractions.Reminders;` at the top.

```csharp
    /// <summary>
    ///     Registers or updates a durable reminder for this grain.
    ///     Requires an <see cref="IReminderService" /> registered in DI (e.g. <c>AddInMemoryReminders()</c>).
    ///     Drop-in equivalent of Orleans' <c>this.RegisterOrUpdateReminder()</c>.
    /// </summary>
    protected Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        string reminderName, TimeSpan dueTime, TimeSpan period)
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException(
                "No IReminderService is registered. Call AddInMemoryReminders() or AddRedisReminders() when building the silo."))
            .RegisterOrUpdateReminderAsync(GrainId, reminderName, dueTime, period);

    /// <summary>
    ///     Cancels a previously registered reminder.
    ///     Drop-in equivalent of Orleans' <c>this.UnregisterReminder()</c>.
    /// </summary>
    protected Task UnregisterReminderAsync(IGrainReminder reminder)
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException("No IReminderService is registered."))
            .UnregisterReminderAsync(GrainId, reminder.ReminderName);

    /// <summary>
    ///     Returns all reminders registered by this grain.
    ///     Drop-in equivalent of Orleans' <c>this.GetReminders()</c>.
    /// </summary>
    protected Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync()
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException("No IReminderService is registered."))
            .GetRemindersAsync(GrainId);
```

- [ ] **Step 2: Add `IRemindable` native dispatch in `LocalGrainCallInvoker`**

In `src/Quark.Runtime/LocalGrainCallInvoker.cs`, add this using:
```csharp
using Quark.Core.Abstractions.Reminders;
```

Then in the `InvokeAsync(GrainId, uint, object?[], CancellationToken)` method, replace the `PostAsync` lambda with:

```csharp
        await activation.PostAsync(async () =>
        {
            try
            {
                object? result;
                if (methodId == ReminderMethodIds.ReceiveReminder &&
                    activation.Grain is IRemindable remindable)
                {
                    await remindable.ReceiveReminder(
                        (string)arguments![0]!,
                        (TickStatus)arguments[1]!).ConfigureAwait(false);
                    result = null;
                }
                else
                {
                    IGrainMethodInvoker invoker = _methodInvokerRegistry.GetInvoker(activation.Grain.GetType());
                    result = await invoker.Invoke(activation.Grain, methodId, arguments).ConfigureAwait(false);
                }
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).ConfigureAwait(false);
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build src/Quark.Core.Abstractions/Quark.Core.Abstractions.csproj
dotnet build src/Quark.Runtime/Quark.Runtime.csproj
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Quark.Core.Abstractions/Grains/Grain.cs \
        src/Quark.Runtime/LocalGrainCallInvoker.cs
git commit -m "feat(F-06): add Grain reminder methods; LocalGrainCallInvoker dispatches IRemindable.ReceiveReminder natively"
```

---

## Task 3: `Quark.Reminders.Abstractions` package + `DefaultReminderService` (TDD)

**Files:**
- Create: `src/Quark.Reminders.Abstractions/Quark.Reminders.Abstractions.csproj`
- Create: `src/Quark.Reminders.Abstractions/IReminderStorage.cs`
- Create: `src/Quark.Reminders.Abstractions/ReminderEntry.cs`
- Create: `src/Quark.Reminders.Abstractions/ReminderOptions.cs`
- Create: `src/Quark.Reminders.Abstractions/GrainReminder.cs`
- Create: `src/Quark.Reminders.Abstractions/DefaultReminderService.cs`
- Modify: `tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
- Create: `tests/Quark.Tests.Unit/Reminders/ReminderServiceTests.cs`

- [ ] **Step 1: Create the project file**

```xml
<!-- src/Quark.Reminders.Abstractions/Quark.Reminders.Abstractions.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Reminders.Abstractions</PackageId>
    <Description>Grain reminder abstractions and default polling service for Quark.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Core.Abstractions\Quark.Core.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions"/>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions"/>
    <PackageReference Include="Microsoft.Extensions.Options"/>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `ReminderEntry.cs`**

```csharp
// src/Quark.Reminders.Abstractions/ReminderEntry.cs
using Quark.Core.Abstractions.Identity;

namespace Quark.Reminders.Abstractions;

/// <summary>The durable record persisted by <see cref="IReminderStorage" /> for each reminder.</summary>
public sealed class ReminderEntry
{
    public required GrainId GrainId { get; init; }
    public required string ReminderName { get; init; }
    public required DateTimeOffset StartAt { get; init; }    // wall-clock when first registered
    public required TimeSpan Period { get; init; }
    public required DateTimeOffset NextFireAt { get; init; } // pre-computed; advanced after each tick
}
```

- [ ] **Step 3: Create `IReminderStorage.cs`**

```csharp
// src/Quark.Reminders.Abstractions/IReminderStorage.cs
using Quark.Core.Abstractions.Identity;

namespace Quark.Reminders.Abstractions;

/// <summary>
///     Provider-level abstraction for persisting reminder entries.
///     Separate from <c>IGrainStorage</c> — reminders require cross-grain queries.
/// </summary>
public interface IReminderStorage
{
    Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default);
    Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default);
    Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `ReminderOptions.cs`**

```csharp
// src/Quark.Reminders.Abstractions/ReminderOptions.cs
namespace Quark.Reminders.Abstractions;

/// <summary>Configuration for the reminder polling service.</summary>
public sealed class ReminderOptions
{
    /// <summary>How often the service checks for due reminders. Default: 1 second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
```

- [ ] **Step 5: Create `GrainReminder.cs`**

```csharp
// src/Quark.Reminders.Abstractions/GrainReminder.cs
using Quark.Core.Abstractions.Reminders;

namespace Quark.Reminders.Abstractions;

internal sealed class GrainReminder(string reminderName) : IGrainReminder
{
    public string ReminderName { get; } = reminderName;
    public bool IsValid { get; } = true;
}
```

- [ ] **Step 6: Add `Quark.Reminders.Abstractions` reference to `Quark.Tests.Unit.csproj`**

In `tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`, add inside the `<ItemGroup>` with project references:

```xml
    <ProjectReference Include="..\..\src\Quark.Reminders.Abstractions\Quark.Reminders.Abstractions.csproj"/>
```

- [ ] **Step 7: Write failing unit tests**

```csharp
// tests/Quark.Tests.Unit/Reminders/ReminderServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Reminders;

public sealed class ReminderServiceTests
{
    private static DefaultReminderService CreateService(
        FakeReminderStorage storage,
        FakeGrainCallInvoker invoker,
        TimeSpan? pollInterval = null)
    {
        var options = Options.Create(new ReminderOptions
        {
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(20)
        });
        return new DefaultReminderService(
            storage, invoker, options, NullLogger<DefaultReminderService>.Instance);
    }

    [Fact]
    public async Task Reminder_FiresAfterDueTime()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker);
        var grainId = new GrainId(new GrainType("TestGrain"), "1");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "tick", TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1));
        await Task.Delay(300);
        await svc.StopAsync(CancellationToken.None);

        Assert.True(invoker.VoidCalls.Count >= 1,
            $"Expected >=1 ReceiveReminder call, got {invoker.VoidCalls.Count}");
        var (calledGrainId, methodId, args) = invoker.VoidCalls[0];
        Assert.Equal(grainId, calledGrainId);
        Assert.Equal(ReminderMethodIds.ReceiveReminder, methodId);
        Assert.Equal("tick", (string)args![0]!);
    }

    [Fact]
    public async Task Reminder_FiresRepeatedly()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker, pollInterval: TimeSpan.FromMilliseconds(15));
        var grainId = new GrainId(new GrainType("TestGrain"), "2");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "repeat", TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));
        await Task.Delay(400);
        await svc.StopAsync(CancellationToken.None);

        Assert.True(invoker.VoidCalls.Count >= 3,
            $"Expected >=3 ReceiveReminder calls, got {invoker.VoidCalls.Count}");
    }

    [Fact]
    public async Task UnregisterReminder_StopsFutureFirings()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker, pollInterval: TimeSpan.FromMilliseconds(20));
        var grainId = new GrainId(new GrainType("TestGrain"), "3");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "stop", TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));
        await Task.Delay(200);
        int countAtUnregister = invoker.VoidCalls.Count;
        await svc.UnregisterReminderAsync(grainId, "stop");
        await Task.Delay(200);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(countAtUnregister, invoker.VoidCalls.Count);
    }

    [Fact]
    public async Task RegisterOrUpdate_UpsertsSingleEntryForSameName()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker);
        var grainId = new GrainId(new GrainType("TestGrain"), "4");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "once", TimeSpan.FromHours(1), TimeSpan.FromHours(24));
        await svc.RegisterOrUpdateReminderAsync(grainId, "once", TimeSpan.FromHours(2), TimeSpan.FromHours(48));
        await svc.StopAsync(CancellationToken.None);

        var entries = await storage.ReadAllAsync();
        Assert.Single(entries);
        Assert.Equal(TimeSpan.FromHours(48), entries[0].Period);
    }

    // ---- Fakes ----

    private sealed class FakeReminderStorage : IReminderStorage
    {
        private readonly Dictionary<string, ReminderEntry> _data = new();

        private static string Key(GrainId id, string name) =>
            $"{id.Type.Value}|{id.Key}|{name}";

        public Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReminderEntry>>(_data.Values.ToList());

        public Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default)
        {
            string prefix = $"{grainId.Type.Value}|{grainId.Key}|";
            var result = _data.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                              .Select(kv => kv.Value).ToList();
            return Task.FromResult<IReadOnlyList<ReminderEntry>>(result);
        }

        public Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
        {
            _data[Key(entry.GrainId, entry.ReminderName)] = entry;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
        {
            _data.Remove(Key(grainId, reminderName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGrainCallInvoker : IGrainCallInvoker
    {
        public List<(GrainId, uint, object?[]?)> VoidCalls { get; } = [];

        public Task<object?> InvokeAsync(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default)
        {
            VoidCalls.Add((grainId, methodId, arguments));
            return Task.FromResult<object?>(null);
        }

        public Task<TResult> InvokeAsync<TResult>(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task InvokeVoidAsync(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default)
        {
            VoidCalls.Add((grainId, methodId, arguments));
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 8: Run tests — expect compile errors (DefaultReminderService not yet created)**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj \
  --filter "FullyQualifiedName~ReminderServiceTests" 2>&1 | head -30
```

Expected: build errors referencing `DefaultReminderService`. Proceed.

- [ ] **Step 9: Create `DefaultReminderService.cs`**

```csharp
// src/Quark.Reminders.Abstractions/DefaultReminderService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;

namespace Quark.Reminders.Abstractions;

/// <summary>
///     Polls <see cref="IReminderStorage" /> on a fixed interval and fires due reminders
///     via <see cref="IGrainCallInvoker" /> using the well-known
///     <see cref="ReminderMethodIds.ReceiveReminder" /> method ID.
///     Register with <c>AddInMemoryReminders()</c> or <c>AddRedisReminders()</c>.
/// </summary>
public sealed class DefaultReminderService : IReminderService, IHostedService, IAsyncDisposable
{
    private readonly IGrainCallInvoker _invoker;
    private readonly ILogger<DefaultReminderService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly IReminderStorage _storage;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>Creates the reminder service.</summary>
    public DefaultReminderService(
        IReminderStorage storage,
        IGrainCallInvoker invoker,
        IOptions<ReminderOptions> options,
        ILogger<DefaultReminderService> logger)
    {
        _storage = storage;
        _invoker = invoker;
        _pollInterval = options.Value.PollInterval;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // ---- IReminderService ----

    /// <inheritdoc />
    public async Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        CancellationToken ct = default)
    {
        var entry = new ReminderEntry
        {
            GrainId = grainId,
            ReminderName = name,
            StartAt = DateTimeOffset.UtcNow,
            Period = period,
            NextFireAt = DateTimeOffset.UtcNow + dueTime
        };
        await _storage.UpsertAsync(entry, ct).ConfigureAwait(false);
        return new GrainReminder(name);
    }

    /// <inheritdoc />
    public Task UnregisterReminderAsync(GrainId grainId, string name, CancellationToken ct = default)
        => _storage.DeleteAsync(grainId, name, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(
        GrainId grainId, CancellationToken ct = default)
    {
        IReadOnlyList<ReminderEntry> entries =
            await _storage.ReadByGrainAsync(grainId, ct).ConfigureAwait(false);
        return entries.Select(static e => (IGrainReminder)new GrainReminder(e.ReminderName)).ToList();
    }

    // ---- IHostedService ----

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_loopTask is not null)
                await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    // ---- Polling loop ----

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await FireDueRemindersAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing due reminders");
            }
        }
    }

    private async Task FireDueRemindersAsync(CancellationToken ct)
    {
        IReadOnlyList<ReminderEntry> all = await _storage.ReadAllAsync(ct).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ReminderEntry entry in all)
        {
            if (entry.NextFireAt > now) continue;

            // Advance NextFireAt BEFORE invoking — at-least-once delivery on crash.
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
```

- [ ] **Step 10: Run tests — expect PASS**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj \
  --filter "FullyQualifiedName~ReminderServiceTests" -v normal
```

Expected: 4 tests pass. Typical output:
```
Passed ReminderServiceTests.Reminder_FiresAfterDueTime
Passed ReminderServiceTests.Reminder_FiresRepeatedly
Passed ReminderServiceTests.UnregisterReminder_StopsFutureFirings
Passed ReminderServiceTests.RegisterOrUpdate_UpsertsSingleEntryForSameName
```

- [ ] **Step 11: Commit**

```bash
git add src/Quark.Reminders.Abstractions/ \
        tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj \
        tests/Quark.Tests.Unit/Reminders/
git commit -m "feat(F-06): add Quark.Reminders.Abstractions with IReminderStorage, DefaultReminderService; unit tests passing"
```

---

## Task 4: `Quark.Reminders.InMemory` package

**Files:**
- Create: `src/Quark.Reminders.InMemory/Quark.Reminders.InMemory.csproj`
- Create: `src/Quark.Reminders.InMemory/InMemoryReminderStorage.cs`
- Create: `src/Quark.Reminders.InMemory/InMemoryReminderServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the project file**

```xml
<!-- src/Quark.Reminders.InMemory/Quark.Reminders.InMemory.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Reminders.InMemory</PackageId>
    <Description>In-memory grain reminder storage provider for Quark.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Reminders.Abstractions\Quark.Reminders.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `InMemoryReminderStorage.cs`**

```csharp
// src/Quark.Reminders.InMemory/InMemoryReminderStorage.cs
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Reminders.Abstractions;

namespace Quark.Reminders.InMemory;

/// <summary>
///     In-memory <see cref="IReminderStorage" /> for development and tests.
///     State is NOT durable across process restarts. Swap for Redis in production.
/// </summary>
public sealed class InMemoryReminderStorage : IReminderStorage
{
    private readonly ConcurrentDictionary<string, ReminderEntry> _store = new(StringComparer.Ordinal);

    private static string GetKey(GrainId grainId, string reminderName)
        => $"{grainId.Type.Value}|{grainId.Key}|{reminderName}";

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ReminderEntry>>(_store.Values.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string prefix = $"{grainId.Type.Value}|{grainId.Key}|";
        var result = _store
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReminderEntry>>(result);
    }

    /// <inheritdoc />
    public Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store[GetKey(entry.GrainId, entry.ReminderName)] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.TryRemove(GetKey(grainId, reminderName), out _);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Create `InMemoryReminderServiceCollectionExtensions.cs`**

```csharp
// src/Quark.Reminders.InMemory/InMemoryReminderServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quark.Reminders.Abstractions;

namespace Quark.Reminders.InMemory;

/// <summary>Service registration helpers for the in-memory reminder provider.</summary>
public static class InMemoryReminderServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-memory reminder storage and polling service.
    ///     Suitable for development and testing. Not durable across restarts.
    ///     Call after <c>AddQuarkRuntime()</c>.
    /// </summary>
    public static IServiceCollection AddInMemoryReminders(
        this IServiceCollection services,
        Action<ReminderOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton<IReminderStorage, InMemoryReminderStorage>();
        services.TryAddSingleton<DefaultReminderService>();
        services.TryAddSingleton<IReminderService>(sp => sp.GetRequiredService<DefaultReminderService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(
            sp => sp.GetRequiredService<DefaultReminderService>()));

        return services;
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/Quark.Reminders.InMemory/Quark.Reminders.InMemory.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Reminders.InMemory/
git commit -m "feat(F-06): add Quark.Reminders.InMemory with InMemoryReminderStorage and DI extensions"
```

---

## Task 5: Integration tests

**Files:**
- Modify: `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj`
- Create: `tests/Quark.Tests.Integration/ReminderIntegrationTests.cs`

- [ ] **Step 1: Add project references to integration test csproj**

In `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <ProjectReference Include="..\..\src\Quark.Reminders.Abstractions\Quark.Reminders.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Reminders.InMemory\Quark.Reminders.InMemory.csproj"/>
```

- [ ] **Step 2: Write failing integration tests**

```csharp
// tests/Quark.Tests.Integration/ReminderIntegrationTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;
using Quark.Reminders.InMemory;
using Quark.Runtime;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

public sealed class ReminderIntegrationTests
{
    private static Action<TestClusterOptions> BuildOptions(
        Action<IServiceCollection>? extraSilo = null,
        TimeSpan? pollInterval = null)
    {
        return options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddInMemoryReminders(o =>
                    o.PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50));
                services.AddGrain<ReminderTestGrain>();
                services.AddGrainMethodInvoker<ReminderTestGrain, ReminderTestGrainMethodInvoker>();
                services.AddGrainActivatorFactory<ReminderTestGrainActivatorFactory>();
                extraSilo?.Invoke(services);
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IReminderTestGrain, ReminderTestGrainProxy>();
            };
        };
    }

    [Fact]
    public async Task Reminder_FiresOnIRemindableGrain()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var grain = cluster.Client.GetGrain<IReminderTestGrain>("fire-test");
        await grain.RegisterReminderAsync("daily", TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(24));

        await Task.Delay(500);

        int count = await grain.GetReceiveCountAsync();
        Assert.True(count >= 1, $"Expected >=1 reminder fires, got {count}");
    }

    [Fact]
    public async Task UnregisterReminder_StopsFutureFirings()
    {
        await using var cluster = await TestCluster.CreateAsync(
            BuildOptions(pollInterval: TimeSpan.FromMilliseconds(30)));

        var grain = cluster.Client.GetGrain<IReminderTestGrain>("unregister-test");
        await grain.RegisterReminderAsync("tick", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);
        int countBeforeUnregister = await grain.GetReceiveCountAsync();
        await grain.UnregisterReminderAsync("tick");
        await Task.Delay(300);
        int countAfterUnregister = await grain.GetReceiveCountAsync();

        Assert.Equal(countBeforeUnregister, countAfterUnregister);
    }

    [Fact]
    public async Task Reminder_SurvivesSimulatedRestart()
    {
        // Use a shared storage instance across both clusters to simulate persistence.
        var storage = new InMemoryReminderStorage();

        Action<IServiceCollection> sharedStorage = services =>
        {
            services.RemoveAll<IReminderStorage>();
            services.AddSingleton<IReminderStorage>(storage);
        };

        await using (var cluster1 = await TestCluster.CreateAsync(
            BuildOptions(sharedStorage, pollInterval: TimeSpan.FromMilliseconds(50))))
        {
            var grain = cluster1.Client.GetGrain<IReminderTestGrain>("restart-test");
            // dueTime = 0 so NextFireAt is already in the past on first load.
            await grain.RegisterReminderAsync("persist", TimeSpan.Zero, TimeSpan.FromHours(24));
            await Task.Delay(200);
        }
        // cluster1 disposed — grain activations gone, storage still has the entry.

        await using var cluster2 = await TestCluster.CreateAsync(
            BuildOptions(sharedStorage, pollInterval: TimeSpan.FromMilliseconds(50)));

        await Task.Delay(300);

        var grain2 = cluster2.Client.GetGrain<IReminderTestGrain>("restart-test");
        int count = await grain2.GetReceiveCountAsync();
        Assert.True(count >= 1, $"Expected reminder to fire after restart, got {count}");
    }

    [Fact]
    public async Task GetReminders_ReturnsRegisteredReminders()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var grain = cluster.Client.GetGrain<IReminderTestGrain>("list-test");
        await grain.RegisterReminderAsync("r1", TimeSpan.FromHours(1), TimeSpan.FromHours(24));
        await grain.RegisterReminderAsync("r2", TimeSpan.FromHours(2), TimeSpan.FromHours(12));

        IReadOnlyList<IGrainReminder> reminders = await grain.GetReminderListAsync();

        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => r.ReminderName == "r1");
        Assert.Contains(reminders, r => r.ReminderName == "r2");
    }

    // ---- Test grain interface ----

    public interface IReminderTestGrain : IGrainWithStringKey
    {
        Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period);
        Task UnregisterReminderAsync(string name);
        Task<int> GetReceiveCountAsync();
        Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync();
    }

    // ---- Test grain implementation ----

    private sealed class ReminderTestGrain : Grain, IRemindable, IReminderTestGrain
    {
        private int _receiveCount;
        private readonly Dictionary<string, IGrainReminder> _handles = new();

        public async Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
        {
            var handle = await RegisterOrUpdateReminderAsync(name, dueTime, period);
            _handles[name] = handle;
        }

        public Task UnregisterReminderAsync(string name)
        {
            if (_handles.TryGetValue(name, out IGrainReminder? handle))
            {
                _handles.Remove(name);
                return UnregisterReminderAsync(handle);
            }
            return Task.CompletedTask;
        }

        public Task<int> GetReceiveCountAsync() => Task.FromResult(_receiveCount);

        public async Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync()
            => await GetRemindersAsync();

        public Task ReceiveReminder(string reminderName, TickStatus status)
        {
            Interlocked.Increment(ref _receiveCount);
            return Task.CompletedTask;
        }
    }

    // ---- Hand-written proxy (client side) ----

    private sealed class ReminderTestGrainProxy(
        GrainId grainId,
        IGrainCallInvoker invoker)
        : IReminderTestGrain,
          IGrainProxyActivator<ReminderTestGrainProxy>
    {
        public static ReminderTestGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
            => invoker.InvokeVoidAsync(grainId, 0u, [name, dueTime, period]);

        public Task UnregisterReminderAsync(string name)
            => invoker.InvokeVoidAsync(grainId, 1u, [name]);

        public Task<int> GetReceiveCountAsync()
            => invoker.InvokeAsync<int>(grainId, 2u, null);

        public Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync()
            => invoker.InvokeAsync<IReadOnlyList<IGrainReminder>>(grainId, 3u, null);
    }

    // ---- Hand-written method invoker (server side) ----
    // Note: ReceiveReminder (methodId 0xFFFF_FF00u) is dispatched natively
    // by LocalGrainCallInvoker — no case needed here.

    private sealed class ReminderTestGrainMethodInvoker : IGrainMethodInvoker
    {
        public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
        {
            var typed = (ReminderTestGrain)grain;
            return methodId switch
            {
                0u => Invoke(typed.RegisterReminderAsync(
                    (string)arguments![0]!, (TimeSpan)arguments[1]!, (TimeSpan)arguments[2]!)),
                1u => Invoke(typed.UnregisterReminderAsync((string)arguments![0]!)),
                2u => await typed.GetReceiveCountAsync(),
                3u => await typed.GetReminderListAsync(),
                _ => throw new NotSupportedException($"Unknown method id {methodId}")
            };
        }

        private static async ValueTask<object?> Invoke(Task t) { await t; return null; }
    }

    // ---- Hand-written activator factory ----

    private sealed class ReminderTestGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(ReminderTestGrain);
        public Grain Create(GrainId grainId, IServiceProvider services) => new ReminderTestGrain();
    }
}
```

- [ ] **Step 3: Run tests — expect failures (IReminderTestGrain.GetReminderListAsync interface mismatch is compile-time, not runtime)**

```bash
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
  --filter "FullyQualifiedName~ReminderIntegrationTests" -v normal
```

Expected: 4 tests PASS. If any fail, check the timing constants — increase `Task.Delay` values on slow CI machines.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
        tests/Quark.Tests.Integration/ReminderIntegrationTests.cs
git commit -m "test(F-06): add ReminderIntegrationTests covering fire, unregister, restart, and list"
```

---

## Task 6: `Quark.Reminders.Redis` package

**Files:**
- Create: `src/Quark.Reminders.Redis/Quark.Reminders.Redis.csproj`
- Create: `src/Quark.Reminders.Redis/RedisReminderStorage.cs`
- Create: `src/Quark.Reminders.Redis/RedisReminderServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the project file**

```xml
<!-- src/Quark.Reminders.Redis/Quark.Reminders.Redis.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Reminders.Redis</PackageId>
    <Description>Redis-backed grain reminder storage provider for Quark.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Reminders.Abstractions\Quark.Reminders.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
    <PackageReference Include="Microsoft.Extensions.Options"/>
    <PackageReference Include="StackExchange.Redis"/>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `RedisReminderStorage.cs`**

```csharp
// src/Quark.Reminders.Redis/RedisReminderStorage.cs
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Reminders.Abstractions;
using StackExchange.Redis;

namespace Quark.Reminders.Redis;

/// <summary>
///     Redis-backed <see cref="IReminderStorage" /> using a single Hash at a configurable key.
///     Entries are serialised as JSON via <c>System.Text.Json</c>.
/// </summary>
public sealed class RedisReminderStorage : IReminderStorage
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _hashKey;

    /// <summary>Creates the Redis reminder storage provider.</summary>
    public RedisReminderStorage(IConnectionMultiplexer redis, IOptions<RedisReminderOptions> options)
    {
        _redis = redis;
        _hashKey = options.Value.HashKey;
    }

    private static string GetField(GrainId grainId, string reminderName)
        => $"{grainId.Type.Value}|{grainId.Key}|{reminderName}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        HashEntry[] fields = await db.HashGetAllAsync(_hashKey).ConfigureAwait(false);
        return fields
            .Select(f => Deserialize(f.Value!))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(
        GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string prefix = $"{grainId.Type.Value}|{grainId.Key}|";
        IDatabase db = _redis.GetDatabase();
        HashEntry[] fields = await db.HashGetAllAsync(_hashKey).ConfigureAwait(false);
        return fields
            .Where(f => ((string?)f.Name)?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .Select(f => Deserialize(f.Value!))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        string field = GetField(entry.GrainId, entry.ReminderName);
        string json = Serialize(entry);
        await db.HashSetAsync(_hashKey, field, json).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        await db.HashDeleteAsync(_hashKey, GetField(grainId, reminderName)).ConfigureAwait(false);
    }

    // ---- Serialisation ----

    private static string Serialize(ReminderEntry entry)
    {
        var dto = new ReminderEntryDto
        {
            GrainType = entry.GrainId.Type.Value,
            GrainKey = entry.GrainId.Key,
            ReminderName = entry.ReminderName,
            StartAtUtcTicks = entry.StartAt.UtcTicks,
            PeriodTicks = entry.Period.Ticks,
            NextFireAtUtcTicks = entry.NextFireAt.UtcTicks
        };
        return JsonSerializer.Serialize(dto);
    }

    private static ReminderEntry? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        ReminderEntryDto? dto = JsonSerializer.Deserialize<ReminderEntryDto>(json);
        if (dto is null) return null;
        return new ReminderEntry
        {
            GrainId = new GrainId(new GrainType(dto.GrainType), dto.GrainKey),
            ReminderName = dto.ReminderName,
            StartAt = new DateTimeOffset(dto.StartAtUtcTicks, TimeSpan.Zero),
            Period = TimeSpan.FromTicks(dto.PeriodTicks),
            NextFireAt = new DateTimeOffset(dto.NextFireAtUtcTicks, TimeSpan.Zero)
        };
    }

    private sealed class ReminderEntryDto
    {
        public string GrainType { get; set; } = "";
        public string GrainKey { get; set; } = "";
        public string ReminderName { get; set; } = "";
        public long StartAtUtcTicks { get; set; }
        public long PeriodTicks { get; set; }
        public long NextFireAtUtcTicks { get; set; }
    }
}
```

- [ ] **Step 3: Create `RedisReminderOptions.cs`**

```csharp
// src/Quark.Reminders.Redis/RedisReminderOptions.cs
using Quark.Reminders.Abstractions;

namespace Quark.Reminders.Redis;

/// <summary>Configuration for the Redis reminder storage provider.</summary>
public sealed class RedisReminderOptions
{
    /// <summary>Redis connection string. Default: localhost:6379.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Redis Hash key under which all reminder entries are stored.</summary>
    public string HashKey { get; set; } = "quark:reminders";

    /// <summary>How often the polling service checks for due reminders. Default: 1 second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
```

- [ ] **Step 4: Create `RedisReminderServiceCollectionExtensions.cs`**

```csharp
// src/Quark.Reminders.Redis/RedisReminderServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quark.Reminders.Abstractions;
using StackExchange.Redis;

namespace Quark.Reminders.Redis;

/// <summary>Service registration helpers for the Redis reminder provider.</summary>
public static class RedisReminderServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Redis-backed reminder storage and polling service.
    ///     Call after <c>AddQuarkRuntime()</c>.
    /// </summary>
    public static IServiceCollection AddRedisReminders(
        this IServiceCollection services,
        Action<RedisReminderOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        // Bridge Redis poll interval → ReminderOptions so DefaultReminderService sees it.
        services.TryAddSingleton<IOptions<ReminderOptions>>(sp =>
        {
            RedisReminderOptions redisOpts = sp.GetRequiredService<IOptions<RedisReminderOptions>>().Value;
            return Options.Create(new ReminderOptions { PollInterval = redisOpts.PollInterval });
        });

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(
                sp.GetRequiredService<IOptions<RedisReminderOptions>>().Value.ConnectionString));

        services.TryAddSingleton<IReminderStorage, RedisReminderStorage>();
        services.TryAddSingleton<DefaultReminderService>();
        services.TryAddSingleton<IReminderService>(sp => sp.GetRequiredService<DefaultReminderService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(
            sp => sp.GetRequiredService<DefaultReminderService>()));

        return services;
    }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/Quark.Reminders.Redis/Quark.Reminders.Redis.csproj
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Reminders.Redis/
git commit -m "feat(F-06): add Quark.Reminders.Redis with RedisReminderStorage and DI extensions"
```

---

## Task 7: Run full test suite

- [ ] **Step 1: Run all tests**

```bash
dotnet test Quark.slnx -v minimal
```

Expected: all existing tests pass + new reminder tests pass. Zero failures.

- [ ] **Step 2: If any pre-existing tests fail, investigate — do not proceed until green**

The two known flaky timing tests (`GrainTimerTests`) may occasionally fail under parallel load; re-run them in isolation if needed:

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GrainTimerTests" -v normal
```

---

## Task 8: Housekeeping — solution file + FEATURES.md

**Files:**
- Modify: `Quark.slnx`
- Modify: `FEATURES.md`

- [ ] **Step 1: Register the three new projects in `Quark.slnx`**

In `Quark.slnx`, inside the `<Folder Name="/src/">` block, add (alphabetical order):

```xml
        <Project Path="src/Quark.Reminders.Abstractions/Quark.Reminders.Abstractions.csproj"/>
        <Project Path="src/Quark.Reminders.InMemory/Quark.Reminders.InMemory.csproj"/>
        <Project Path="src/Quark.Reminders.Redis/Quark.Reminders.Redis.csproj"/>
```

- [ ] **Step 2: Verify solution builds**

```bash
dotnet build Quark.slnx
```

Expected: 0 errors.

- [ ] **Step 3: Mark F-06 complete in `FEATURES.md`**

Change the F-06 line from:

```markdown
- [ ] **F-06** Grain Reminders (`IRemindable`, `RegisterOrUpdateReminder`, `IGrainReminder`, durable `IReminderService`) — _Complexity: L_
```

to:

```markdown
- [x] **F-06** Grain Reminders (`IRemindable`, `RegisterOrUpdateReminder`, `IGrainReminder`, durable `IReminderService`) — _Complexity: L_
```

- [ ] **Step 4: Final commit**

```bash
git add Quark.slnx FEATURES.md
git commit -m "docs: mark F-06 complete; register Quark.Reminders.* packages in solution"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task(s) |
|---|---|
| `IRemindable`, `IGrainReminder`, `TickStatus` in Core.Abstractions | Task 1 |
| `IReminderService` interface + `IGrainContext.ReminderService` gateway | Task 1 |
| `ReminderMethodIds.ReceiveReminder = 0xFFFF_FF00u` | Task 1 |
| `Grain` protected reminder methods | Task 2 |
| Native `IRemindable` dispatch in `LocalGrainCallInvoker` | Task 2 |
| `IReminderStorage`, `ReminderEntry`, `ReminderOptions` | Task 3 |
| `DefaultReminderService` — fixed polling, at-least-once, `IHostedService` | Task 3 |
| Unit tests: fire, repeat, unregister, upsert-same-name | Task 3 |
| `InMemoryReminderStorage` + `AddInMemoryReminders()` | Task 4 |
| Integration tests: fire, unregister, restart, list | Task 5 |
| `RedisReminderStorage` + `AddRedisReminders()` | Task 6 |
| `Quark.slnx` + `FEATURES.md` | Task 8 |

All spec requirements covered. ✓
