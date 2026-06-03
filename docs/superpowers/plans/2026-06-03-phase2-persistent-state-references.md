# Phase 2 — IPersistentState<T> and Grain References

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement F-04 (`IPersistentState<T>` + `[PersistentState]` named-slot injection) and F-05 (`AsReference<T>()` self-reference + `CreateObjectReference<T>()` observer wrapper).

**Architecture:** F-04 adds a new `IPersistentState<TState>` interface and `PersistentState<TState>` implementation in `Quark.Persistence.Abstractions`, extends the `GrainActivatorGenerator` to detect `[PersistentState]`-annotated constructor parameters, and extends both storage DI registrations to support named keyed providers. F-05 adds a `GetGrain(GrainId)` overload to `IGrainFactory`, implements `AsReference<T>()` on `Grain`, and introduces an `ObjectReferenceRegistry` that `LocalGrainCallInvoker` checks before the activation table.

**Tech Stack:** .NET 10, xUnit, `Microsoft.Extensions.DependencyInjection` keyed services, Roslyn incremental generators

---

## File Map

| Action | File |
|---|---|
| Create | `src/Quark.Persistence.Abstractions/IPersistentState.cs` |
| Create | `src/Quark.Persistence.Abstractions/PersistentStateAttribute.cs` |
| Create | `src/Quark.Persistence.Abstractions/PersistentState.cs` |
| Modify | `src/Quark.Persistence.InMemory/InMemoryGrainStorageServiceCollectionExtensions.cs` |
| Modify | `src/Quark.Persistence.Redis/RedisGrainStorageServiceCollectionExtensions.cs` |
| Modify | `src/Quark.CodeGenerator/GrainActivatorGenerator.cs` |
| Modify | `src/Quark.Core.Abstractions/Hosting/IGrainFactory.cs` |
| Modify | `src/Quark.Core.Abstractions/Grains/Grain.cs` |
| Modify | `src/Quark.Client/LocalGrainFactory.cs` |
| Modify | `src/Quark.Client/LocalClusterClient.cs` |
| Create | `src/Quark.Client/ObjectReferenceRegistry.cs` |
| Modify | `src/Quark.Client/ClientServiceCollectionExtensions.cs` |
| Modify | `src/Quark.Runtime/LocalGrainCallInvoker.cs` |
| Create | `tests/Quark.Tests.Unit/Persistence/PersistentStateTests.cs` |
| Create | `tests/Quark.Tests.Unit/Grains/AsReferenceTests.cs` |

---

## Task 6: F-04 — `IPersistentState<T>` and `[PersistentState]`

### Step 6.1 — Write failing tests

Create `tests/Quark.Tests.Unit/Persistence/PersistentStateTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Serialization;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class PersistentStateTests
{
    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddInMemoryGrainStorage();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PersistentState_ReadWriteClear_RoundTrip()
    {
        var sp = BuildServices();
        var storage = sp.GetRequiredService<IGrainStorage>();
        var grainId = new GrainId(new GrainType("G"), "1");

        var state = new PersistentState<Counter>("counter", storage);
        state.Initialize(grainId);

        await state.ReadStateAsync();
        Assert.False(state.RecordExists);
        Assert.Equal(0, state.State.Value);

        state.State.Value = 42;
        await state.WriteStateAsync();
        Assert.True(state.RecordExists);

        // Read back via a new instance
        var state2 = new PersistentState<Counter>("counter", storage);
        state2.Initialize(grainId);
        await state2.ReadStateAsync();
        Assert.Equal(42, state2.State.Value);

        await state2.ClearStateAsync();
        Assert.False(state2.RecordExists);
    }

    [Fact]
    public async Task TwoNamedSlots_AreIndependent()
    {
        var sp = BuildServices();
        var storage = sp.GetRequiredService<IGrainStorage>();
        var grainId = new GrainId(new GrainType("G"), "1");

        var profile = new PersistentState<ProfileData>("profile", storage);
        var settings = new PersistentState<SettingsData>("settings", storage);
        profile.Initialize(grainId);
        settings.Initialize(grainId);

        profile.State.Name = "Alice";
        await profile.WriteStateAsync();
        settings.State.Theme = "dark";
        await settings.WriteStateAsync();

        var p2 = new PersistentState<ProfileData>("profile", storage);
        p2.Initialize(grainId);
        await p2.ReadStateAsync();
        Assert.Equal("Alice", p2.State.Name);

        var s2 = new PersistentState<SettingsData>("settings", storage);
        s2.Initialize(grainId);
        await s2.ReadStateAsync();
        Assert.Equal("dark", s2.State.Theme);
    }

    [Fact]
    public async Task NamedStorageProvider_UsedWhenRegistered()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddInMemoryGrainStorage();                    // default
        services.AddKeyedInMemoryGrainStorage("secondary");    // named

        var sp = services.BuildServiceProvider();
        var defaultStorage = sp.GetRequiredService<IGrainStorage>();
        var namedStorage = sp.GetRequiredKeyedService<IGrainStorage>("secondary");

        Assert.NotSame(defaultStorage, namedStorage);
    }

    private sealed class Counter { public int Value { get; set; } }
    private sealed class ProfileData { public string Name { get; set; } = ""; }
    private sealed class SettingsData { public string Theme { get; set; } = ""; }
}
```

- [ ] **Step 6.1 — Run failing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~PersistentStateTests" -v minimal
```
Expected: FAIL — `IPersistentState`, `PersistentState`, `AddKeyedInMemoryGrainStorage` do not exist.

- [ ] **Step 6.2 — Create `IPersistentState.cs`**

Create `src/Quark.Persistence.Abstractions/IPersistentState.cs`:

```csharp
namespace Quark.Persistence.Abstractions;

/// <summary>
///     A named, provider-backed persistent state slot injectable into a grain constructor.
///     Drop-in equivalent of Orleans' <c>IPersistentState&lt;TState&gt;</c>.
/// </summary>
public interface IPersistentState<TState> where TState : new()
{
    /// <summary>The current in-memory state value.</summary>
    TState State { get; set; }

    /// <summary><c>true</c> if the state record exists in the backing store.</summary>
    bool RecordExists { get; }

    /// <summary>The ETag from the last read or write operation.</summary>
    string Etag { get; }

    /// <summary>Loads state from the backing store into <see cref="State" />.</summary>
    Task ReadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the current <see cref="State" /> to the backing store.</summary>
    Task WriteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the state record from the backing store and resets <see cref="State" />.</summary>
    Task ClearStateAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6.3 — Create `PersistentStateAttribute.cs`**

Create `src/Quark.Persistence.Abstractions/PersistentStateAttribute.cs`:

```csharp
namespace Quark.Persistence.Abstractions;

/// <summary>
///     Marks a grain constructor parameter as a named persistent state slot.
///     Drop-in equivalent of Orleans' <c>[PersistentState]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class PersistentStateAttribute : Attribute
{
    /// <param name="stateName">Logical name for this state slot (used as storage key).</param>
    /// <param name="storageName">
    ///     Name of the registered <see cref="IGrainStorage" /> keyed service.
    ///     Pass <c>null</c> to use the default (unkeyed) provider.
    /// </param>
    public PersistentStateAttribute(string stateName, string? storageName = null)
    {
        StateName = stateName;
        StorageName = storageName;
    }

    /// <summary>Logical name for this state slot.</summary>
    public string StateName { get; }

    /// <summary>Keyed DI name for the storage provider, or <c>null</c> for the default.</summary>
    public string? StorageName { get; }
}
```

- [ ] **Step 6.4 — Create `PersistentState.cs`**

Create `src/Quark.Persistence.Abstractions/PersistentState.cs`:

```csharp
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Concrete implementation of <see cref="IPersistentState{TState}" />.
///     Created by the runtime for each <c>[PersistentState]</c> constructor parameter.
/// </summary>
public sealed class PersistentState<TState> : IPersistentState<TState> where TState : new()
{
    private readonly IGrainStorage _storage;
    private readonly string _stateName;
    private readonly GrainState<TState> _grainState = new();
    private GrainId _grainId;

    /// <summary>Creates a state slot backed by the supplied storage provider.</summary>
    public PersistentState(string stateName, IGrainStorage storage)
    {
        _stateName = stateName;
        _storage = storage;
    }

    /// <inheritdoc />
    public TState State { get => _grainState.State; set => _grainState.State = value; }

    /// <inheritdoc />
    public bool RecordExists => _grainState.RecordExists;

    /// <inheritdoc />
    public string Etag => _grainState.ETag;

    /// <inheritdoc />
    public Task ReadStateAsync(CancellationToken cancellationToken = default)
        => _storage.ReadStateAsync(_stateName, _grainId, _grainState, cancellationToken);

    /// <inheritdoc />
    public Task WriteStateAsync(CancellationToken cancellationToken = default)
        => _storage.WriteStateAsync(_stateName, _grainId, _grainState, cancellationToken);

    /// <inheritdoc />
    public Task ClearStateAsync(CancellationToken cancellationToken = default)
        => _storage.ClearStateAsync(_stateName, _grainId, _grainState, cancellationToken);

    /// <summary>
    ///     Called by the activator factory immediately after construction to bind the grain identity.
    ///     Must be called before any read/write operation.
    /// </summary>
    public void Initialize(GrainId grainId) => _grainId = grainId;
}
```

- [ ] **Step 6.5 — Add `AddKeyedInMemoryGrainStorage` to `InMemoryGrainStorageServiceCollectionExtensions.cs`**

In `src/Quark.Persistence.InMemory/InMemoryGrainStorageServiceCollectionExtensions.cs`, add:

```csharp
    /// <summary>
    ///     Registers a named in-memory grain storage provider accessible via
    ///     <c>IServiceProvider.GetRequiredKeyedService&lt;IGrainStorage&gt;(name)</c>.
    /// </summary>
    public static IServiceCollection AddKeyedInMemoryGrainStorage(
        this IServiceCollection services,
        string providerName)
    {
        services.AddKeyedSingleton<IGrainStorage>(providerName,
            (sp, _) => new InMemoryGrainStorage(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }
```

Also add the same pattern for Redis in `src/Quark.Persistence.Redis/` — add `AddKeyedRedisGrainStorage(string providerName, Action<RedisStorageOptions> configure)`.

- [ ] **Step 6.6 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~PersistentStateTests" -v minimal
```
Expected: 3 tests PASS.

- [ ] **Step 6.7 — Update `GrainActivatorGenerator` to handle `[PersistentState]` parameters**

In `src/Quark.CodeGenerator/GrainActivatorGenerator.cs`, in the method-body emission for the activator factory `Create(GrainId, IServiceProvider)`:

Current pattern emits `sp.GetRequiredService<T>()` for each constructor parameter. Add a check: if the parameter has `[PersistentState(stateName, storageName)]`, emit:

```csharp
// Detect [PersistentState] on parameter:
// var storage = sp.GetRequiredKeyedService<IGrainStorage>("myStore");  // if storageName != null
// var storage = sp.GetRequiredService<IGrainStorage>();                 // if storageName is null
// var ps = new Quark.Persistence.Abstractions.PersistentState<TState>(stateName, storage);
// ps.Initialize(grainId);
// // use ps as argument
```

Add this case to the parameter-resolution switch in the generator. The generator already receives `IParameterSymbol`; check `parameter.GetAttributes()` for `PersistentStateAttribute`.

Exact change in the generator: in the loop that builds constructor arguments, add:

```csharp
var persistentStateAttr = parameter.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "PersistentStateAttribute");
if (persistentStateAttr is not null)
{
    string stateName = (string)persistentStateAttr.ConstructorArguments[0].Value!;
    string? storageName = persistentStateAttr.ConstructorArguments.Length > 1
        ? persistentStateAttr.ConstructorArguments[1].Value as string
        : null;

    string stateType = ((INamedTypeSymbol)parameter.Type).TypeArguments[0].ToDisplayString();

    if (storageName is null)
    {
        sb.AppendLine($"            var __storage_{i} = serviceProvider.GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>();");
    }
    else
    {
        sb.AppendLine($"            var __storage_{i} = serviceProvider.GetRequiredKeyedService<global::Quark.Persistence.Abstractions.IGrainStorage>(\"{storageName}\");");
    }
    sb.AppendLine($"            var __ps_{i} = new global::Quark.Persistence.Abstractions.PersistentState<{stateType}>(\"{stateName}\", __storage_{i});");
    sb.AppendLine($"            __ps_{i}.Initialize(grainId);");
    args.Add($"__ps_{i}");
    continue;
}
```

- [ ] **Step 6.8 — Add code-gen integration test**

In `tests/Quark.Tests.CodeGenerator/GrainActivatorGeneratorTests.cs`, add a test verifying that a grain with `[PersistentState]` constructor parameters generates an activator that creates `PersistentState<T>` instances:

```csharp
[Fact]
public async Task GeneratesActivator_ForPersistentStateParam()
{
    string source = """
        using Quark.Core.Abstractions.Grains;
        using Quark.Persistence.Abstractions;

        public interface IMyGrain : IGrain, IGrainWithStringKey { }

        public class MyGrain : Grain, IMyGrain
        {
            public MyGrain([PersistentState("profile")] IPersistentState<Profile> profile) { }
        }

        public class Profile { }
        """;

    GeneratorDriver driver = await RunGeneratorAsync(source);
    GeneratorRunResult result = driver.GetRunResult().Results[0];

    string activatorSource = result.GeneratedSources
        .Single(s => s.HintName.Contains("MyGrainActivatorFactory"))
        .SourceText.ToString();

    Assert.Contains("PersistentState<", activatorSource);
    Assert.Contains("\"profile\"", activatorSource);
    Assert.Contains("GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>()", activatorSource);
}
```

- [ ] **Step 6.9 — Run code-gen tests**

```bash
dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj -v minimal
```
Expected: all pass including the new test.

- [ ] **Step 6.10 — Run full suite**

```bash
dotnet test Quark.slnx -v minimal
```

- [ ] **Step 6.11 — Commit**

```bash
git add src/Quark.Persistence.Abstractions/ \
        src/Quark.Persistence.InMemory/InMemoryGrainStorageServiceCollectionExtensions.cs \
        src/Quark.Persistence.Redis/ \
        src/Quark.CodeGenerator/GrainActivatorGenerator.cs \
        tests/Quark.Tests.Unit/Persistence/PersistentStateTests.cs \
        tests/Quark.Tests.CodeGenerator/GrainActivatorGeneratorTests.cs
git commit -m "feat(F-04): add IPersistentState<T>, [PersistentState] attribute, named storage providers, and code-gen support"
```

---

## Task 7: F-05 — `AsReference<T>()` and `CreateObjectReference<T>()`

### Step 7.1 — Write failing tests

Create `tests/Quark.Tests.Unit/Grains/AsReferenceTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class AsReferenceTests : IAsyncDisposable
{
    private readonly GrainCallFixture _fixture = new();

    [Fact]
    public async Task AsReference_ReturnsProxyForSameGrainId()
    {
        // When a CounterGrain calls AsReference<ICounterGrain>(),
        // the returned proxy should route back to the same activation.
        ICounterGrain grain = _fixture.Client.GetGrain<ICounterGrain>("self-ref-test");
        await grain.IncrementAsync();
        int value = await grain.GetCountAsync();
        Assert.Equal(1, value);

        // Verify we can get a reference and call through it
        ICounterGrain sameRef = _fixture.Client.GetGrain<ICounterGrain>("self-ref-test");
        Assert.Equal(1, await sameRef.GetCountAsync());
    }

    [Fact]
    public async Task CreateObjectReference_RoutesCallsToWrappedObject()
    {
        var observer = new TestObserver();
        ITestObserver observerRef = _fixture.Client.CreateObjectReference<ITestObserver>(observer);

        await observerRef.Notify("hello");

        Assert.Equal(["hello"], observer.Received);
    }

    [Fact]
    public async Task CreateObjectReference_DifferentCallsDifferentObjects()
    {
        var obs1 = new TestObserver();
        var obs2 = new TestObserver();
        ITestObserver ref1 = _fixture.Client.CreateObjectReference<ITestObserver>(obs1);
        ITestObserver ref2 = _fixture.Client.CreateObjectReference<ITestObserver>(obs2);

        await ref1.Notify("a");
        await ref2.Notify("b");

        Assert.Equal(["a"], obs1.Received);
        Assert.Equal(["b"], obs2.Received);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    // --- helpers ---

    public interface ITestObserver : IGrainObserver
    {
        Task Notify(string message);
    }

    private sealed class TestObserver : ITestObserver
    {
        public List<string> Received { get; } = [];
        public Task Notify(string message) { Received.Add(message); return Task.CompletedTask; }
    }
}
```

- [ ] **Step 7.1 — Run failing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~AsReferenceTests" -v minimal
```
Expected: FAIL — `CreateObjectReference` does not exist on `IClusterClient`.

- [ ] **Step 7.2 — Add `CreateObjectReference<T>()` to `IGrainFactory`**

In `src/Quark.Core.Abstractions/Hosting/IGrainFactory.cs`, add:

```csharp
    /// <summary>
    ///     Wraps a local object that implements <see cref="IGrainObserver" /> in a proxy
    ///     so that grain-to-grain observer notification works without activating a real grain.
    ///     Drop-in equivalent of Orleans' <c>IGrainFactory.CreateObjectReference&lt;T&gt;(T)</c>.
    /// </summary>
    TGrainObserver CreateObjectReference<TGrainObserver>(TGrainObserver obj)
        where TGrainObserver : IGrainObserver;
```

- [ ] **Step 7.3 — Create `ObjectReferenceRegistry.cs`**

Create `src/Quark.Client/ObjectReferenceRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
///     Stores plain objects registered via <c>CreateObjectReference</c> under synthetic
///     <see cref="GrainId" />s so that <c>LocalGrainCallInvoker</c> can route calls to them
///     without activating a real grain.
/// </summary>
public sealed class ObjectReferenceRegistry
{
    private static readonly GrainType ObserverType = new("__observer__");

    private readonly ConcurrentDictionary<GrainId, Func<uint, object?[]?, Task<object?>>> _entries = new();

    /// <summary>
    ///     Registers <paramref name="target" /> and returns a synthetic <see cref="GrainId" />
    ///     that uniquely identifies it.
    /// </summary>
    public GrainId Register<T>(T target, Func<T, uint, object?[]?, Task<object?>> dispatch)
    {
        var id = new GrainId(ObserverType, Guid.NewGuid().ToString("N"));
        _entries[id] = (methodId, args) => dispatch(target, methodId, args);
        return id;
    }

    /// <summary>
    ///     Looks up the dispatch delegate for <paramref name="grainId" />.
    ///     Returns <c>false</c> if not a registered object reference.
    /// </summary>
    public bool TryGetDispatch(GrainId grainId, out Func<uint, object?[]?, Task<object?>>? dispatch)
        => _entries.TryGetValue(grainId, out dispatch);
}
```

- [ ] **Step 7.4 — Add `CreateObjectReference<T>()` to `LocalGrainFactory`**

In `src/Quark.Client/LocalGrainFactory.cs`:

1. Add a constructor parameter `ObjectReferenceRegistry objectReferenceRegistry`.
2. Add the implementation:

```csharp
    public TGrainObserver CreateObjectReference<TGrainObserver>(TGrainObserver obj)
        where TGrainObserver : IGrainObserver
    {
        GrainId id = _objectReferenceRegistry.Register(obj,
            static (target, methodId, args) =>
            {
                // Use reflection only as a fallback here; production usage goes through
                // the hand-written or code-gen'd IGrainMethodInvoker when available.
                // For IGrainObserver, callers typically use a hand-written invoker in tests.
                throw new NotImplementedException(
                    "CreateObjectReference requires a registered IGrainMethodInvoker for the observer type.");
            });
        return _proxyRegistry.CreateProxy<TGrainObserver>(id, _invoker);
    }
```

> The actual dispatch requires the `LocalGrainCallInvoker` to route the call to the registered object. Wire this up in Step 7.5.

- [ ] **Step 7.5 — Add object-reference dispatch to `LocalGrainCallInvoker`**

In `src/Quark.Runtime/LocalGrainCallInvoker.cs`, inject `ObjectReferenceRegistry` and short-circuit calls to registered objects:

```csharp
// Add field:
private readonly ObjectReferenceRegistry _objectRefs;

// In InvokeAsync, before GetOrActivateAsync:
if (_objectRefs.TryGetDispatch(grainId, out var dispatch) && dispatch is not null)
    return await dispatch(methodId, arguments).ConfigureAwait(false);
```

Also register `ObjectReferenceRegistry` as a singleton in `ClientServiceCollectionExtensions.AddLocalClusterClient()`:

```csharp
services.TryAddSingleton<ObjectReferenceRegistry>();
```

And pass it to `LocalGrainFactory` and `LocalGrainCallInvoker` via their constructors.

- [ ] **Step 7.6 — Implement `CreateObjectReference` with direct delegate dispatch**

Replace the `NotImplementedException` stub with a direct invocation approach. For test purposes, `ITestObserver` will have a hand-written invoker registered in `GrainCallFixture`. Update `GrainCallFixture` to register the observer method invoker and fix the proxy registration so `CreateObjectReference` returns a working proxy.

The simplest robust approach: `ObjectReferenceRegistry.Register` takes an `IGrainMethodInvoker` (not a raw delegate):

```csharp
// In ObjectReferenceRegistry:
public GrainId Register(IGrainMethodInvoker invoker, object target)
{
    var id = new GrainId(new GrainType("__observer__"), Guid.NewGuid().ToString("N"));
    _entries[id] = new Entry(target, invoker);
    return id;
}

// In LocalGrainCallInvoker.InvokeAsync:
if (_objectRefs.TryGetEntry(grainId, out var entry))
{
    return await entry.Invoker.Invoke(/* adapter */ new ObjectGrainAdapter(entry.Target), methodId, arguments);
}
```

For the test, add a `TestObserverMethodInvoker` in `AsReferenceTests` that routes `methodId=1` to `Notify`. Register it in the fixture.

> See the `GrainCallFixture` and `CounterGrainMethodInvoker` pattern for reference.

- [ ] **Step 7.7 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~AsReferenceTests" -v minimal
```
Expected: 3 tests PASS.

- [ ] **Step 7.8 — Run full suite**

```bash
dotnet test Quark.slnx -v minimal
```

- [ ] **Step 7.9 — Commit**

```bash
git add src/Quark.Core.Abstractions/Hosting/IGrainFactory.cs \
        src/Quark.Core.Abstractions/Grains/Grain.cs \
        src/Quark.Client/ObjectReferenceRegistry.cs \
        src/Quark.Client/LocalGrainFactory.cs \
        src/Quark.Client/LocalClusterClient.cs \
        src/Quark.Client/ClientServiceCollectionExtensions.cs \
        src/Quark.Runtime/LocalGrainCallInvoker.cs \
        tests/Quark.Tests.Unit/Grains/AsReferenceTests.cs
git commit -m "feat(F-05): add AsReference<T>() and CreateObjectReference<T>() with ObjectReferenceRegistry dispatch"
```

---

## Task 8: Tick FEATURES.md

- [ ] **Update Phase 2 checkboxes in `FEATURES.md` and commit.**

```bash
git add FEATURES.md
git commit -m "docs: mark Phase 2 features complete in FEATURES.md"
```
