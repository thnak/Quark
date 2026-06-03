# Phase 1 — Primary Key Helpers, Reentrant, Timers, OpenTelemetry

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement F-01 (primary key accessors), F-02 (`[Reentrant]`), F-03 (grain timers), and F-11 (OpenTelemetry propagation) — all four are independent and produce working, tested features on their own.

**Architecture:** F-01 adds helpers to `Grain` base; F-02 adds a new attribute and changes `GrainActivation` dispatch; F-03 introduces `IGrainTimer`/`GrainTimerCreationOptions` in abstractions and a `GrainTimer<TState>` in the runtime wired through `GrainContext`; F-11 adds `ActivitySource` instrumentation to `LocalGrainCallInvoker`.

**Tech Stack:** .NET 10, xUnit, `System.Threading.Timer`, `System.Diagnostics.ActivitySource`

---

## File Map

| Action | File |
|---|---|
| Modify | `src/Quark.Core.Abstractions/Grains/Grain.cs` |
| Modify | `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs` |
| Create | `src/Quark.Core.Abstractions/Grains/ReentrantAttribute.cs` |
| Create | `src/Quark.Core.Abstractions/Timers/IGrainTimer.cs` |
| Create | `src/Quark.Core.Abstractions/Timers/GrainTimerCreationOptions.cs` |
| Create | `src/Quark.Runtime/GrainTimer.cs` |
| Modify | `src/Quark.Runtime/GrainContext.cs` |
| Modify | `src/Quark.Runtime/GrainActivation.cs` |
| Modify | `src/Quark.Runtime/LocalGrainCallInvoker.cs` |
| Modify | `src/Quark.Core/Hosting/SiloBuilderExtensions.cs` |
| Create | `tests/Quark.Tests.Unit/Grains/PrimaryKeyTests.cs` |
| Create | `tests/Quark.Tests.Unit/Grains/ReentrantTests.cs` |
| Create | `tests/Quark.Tests.Unit/Grains/GrainTimerTests.cs` |
| Create | `tests/Quark.Tests.Unit/Diagnostics/ActivityPropagationTests.cs` |

---

## Task 1: F-01 — Primary key accessor helpers

### Files
- Modify: `src/Quark.Core.Abstractions/Grains/Grain.cs`
- Create: `tests/Quark.Tests.Unit/Grains/PrimaryKeyTests.cs`

- [ ] **Step 1.1 — Write failing tests**

Create `tests/Quark.Tests.Unit/Grains/PrimaryKeyTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class PrimaryKeyTests
{
    private static readonly IGrainFactory NullFactory = new NullGrainFactory();
    private static readonly IServiceProvider NullServices = new NullServiceProvider();

    private static async Task<T> ActivateAsync<T>(T grain, GrainId id)
        where T : Grain
    {
        var ctx = new GrainContext(id, NullFactory, NullServices);
        await ctx.ActivateAsync(grain);
        return grain;
    }

    [Fact]
    public async Task GetPrimaryKeyString_ReturnsKey()
    {
        var grain = await ActivateAsync(new StringKeyTestGrain(),
            new GrainId(new GrainType("G"), "hello"));
        Assert.Equal("hello", grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKey_Guid_ReturnsKey()
    {
        Guid id = Guid.NewGuid();
        var grain = await ActivateAsync(new GuidKeyTestGrain(),
            new GrainId(new GrainType("G"), id.ToString("N")));
        Assert.Equal(id, grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKeyLong_ReturnsKey()
    {
        var grain = await ActivateAsync(new LongKeyTestGrain(),
            new GrainId(new GrainType("G"), "42"));
        Assert.Equal(42L, grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKey_GuidCompound_ReturnsKeyAndExtension()
    {
        Guid id = Guid.NewGuid();
        var grain = await ActivateAsync(new GuidCompoundTestGrain(),
            new GrainId(new GrainType("G"), $"{id:N}+ext"));
        grain.ReadKey(out string ext);
        Assert.Equal(id, grain.ReadKey(out _));
        Assert.Equal("ext", ext);
    }

    [Fact]
    public async Task GetPrimaryKeyLong_Compound_ReturnsKeyAndExtension()
    {
        var grain = await ActivateAsync(new LongCompoundTestGrain(),
            new GrainId(new GrainType("G"), "99+region"));
        Assert.Equal(99L, grain.ReadKey(out string ext));
        Assert.Equal("region", ext);
    }

    // --- test grain helpers ---

    private sealed class StringKeyTestGrain : Grain
    {
        public string ReadKey() => GetPrimaryKeyString();
    }

    private sealed class GuidKeyTestGrain : Grain
    {
        public Guid ReadKey() => GetPrimaryKey();
    }

    private sealed class LongKeyTestGrain : Grain
    {
        public long ReadKey() => GetPrimaryKeyLong();
    }

    private sealed class GuidCompoundTestGrain : Grain
    {
        public Guid ReadKey(out string ext) => GetPrimaryKey(out ext);
    }

    private sealed class LongCompoundTestGrain : Grain
    {
        public long ReadKey(out string ext) => GetPrimaryKeyLong(out ext);
    }
}
```

- [ ] **Step 1.2 — Run failing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~PrimaryKeyTests" -v minimal
```
Expected: FAIL — `GetPrimaryKeyString`, `GetPrimaryKey`, `GetPrimaryKeyLong` do not exist on `Grain`.

- [ ] **Step 1.3 — Add helpers to `Grain.cs`**

Add the following protected methods to `src/Quark.Core.Abstractions/Grains/Grain.cs` (after the `DelayDeactivation` method, before the internal `SetContext`):

```csharp
    /// <summary>Returns the string key of this grain. Only valid on <see cref="IGrainWithStringKey" /> grains.</summary>
    protected string GetPrimaryKeyString() => GrainId.Key;

    /// <summary>Returns the <see cref="Guid" /> key of this grain. Only valid on <see cref="IGrainWithGuidKey" /> grains.</summary>
    protected Guid GetPrimaryKey() => Guid.ParseExact(GrainId.Key, "N");

    /// <summary>Returns the <see cref="long" /> key of this grain. Only valid on <see cref="IGrainWithIntegerKey" /> grains.</summary>
    protected long GetPrimaryKeyLong() => long.Parse(GrainId.Key, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    ///     Returns the <see cref="Guid" /> key and extension of this grain.
    ///     Only valid on <see cref="IGrainWithGuidCompoundKey" /> grains.
    /// </summary>
    protected Guid GetPrimaryKey(out string keyExtension)
    {
        int plus = GrainId.Key.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            keyExtension = string.Empty;
            return Guid.ParseExact(GrainId.Key, "N");
        }
        keyExtension = GrainId.Key[(plus + 1)..];
        return Guid.ParseExact(GrainId.Key[..plus], "N");
    }

    /// <summary>
    ///     Returns the <see cref="long" /> key and extension of this grain.
    ///     Only valid on <see cref="IGrainWithIntegerCompoundKey" /> grains.
    /// </summary>
    protected long GetPrimaryKeyLong(out string keyExtension)
    {
        int plus = GrainId.Key.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            keyExtension = string.Empty;
            return long.Parse(GrainId.Key, System.Globalization.CultureInfo.InvariantCulture);
        }
        keyExtension = GrainId.Key[(plus + 1)..];
        return long.Parse(GrainId.Key[..plus], System.Globalization.CultureInfo.InvariantCulture);
    }
```

- [ ] **Step 1.4 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~PrimaryKeyTests" -v minimal
```
Expected: 5 tests PASS.

- [ ] **Step 1.5 — Run full suite to confirm no regressions**

```bash
dotnet test Quark.slnx -v minimal
```
Expected: all pre-existing tests pass.

- [ ] **Step 1.6 — Commit**

```bash
git add src/Quark.Core.Abstractions/Grains/Grain.cs tests/Quark.Tests.Unit/Grains/PrimaryKeyTests.cs
git commit -m "feat(F-01): add GetPrimaryKeyString/GetPrimaryKey/GetPrimaryKeyLong helpers to Grain"
```

---

## Task 2: F-02 — `[Reentrant]` attribute

### Files
- Create: `src/Quark.Core.Abstractions/Grains/ReentrantAttribute.cs`
- Modify: `src/Quark.Runtime/GrainActivation.cs`
- Create: `tests/Quark.Tests.Unit/Grains/ReentrantTests.cs`

- [ ] **Step 2.1 — Write failing test**

Create `tests/Quark.Tests.Unit/Grains/ReentrantTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class ReentrantTests : IAsyncDisposable
{
    // We build a minimal in-process fixture directly in this test
    // using the same pattern as GrainCallFixture.

    [Fact]
    public async Task NonReentrantGrain_SerializesCallsBehindQueue()
    {
        // Two calls to a non-reentrant grain that each take 50ms.
        // Total wall time should be ~100ms (serial), not ~50ms (parallel).
        var results = new List<string>();
        var fixture = new SimpleGrainFixture<ISerialGrain, SerialGrain>("SerialGrain",
            (id, invoker) => new SerialGrainProxy(id, invoker),
            () => new SerialGrain());

        ISerialGrain grain = fixture.Client.GetGrain<ISerialGrain>("g");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(grain.SlowAppend("A"), grain.SlowAppend("B"));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected serial execution (~100ms), got {sw.ElapsedMilliseconds}ms");

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ReentrantGrain_AllowsConcurrentExecution()
    {
        // Two calls to a reentrant grain that each take 50ms.
        // Total wall time should be ~50ms (parallel), not ~100ms (serial).
        var fixture = new SimpleGrainFixture<IReentrantGrain, ReentrantTestGrain>("ReentrantTestGrain",
            (id, invoker) => new ReentrantTestGrainProxy(id, invoker),
            () => new ReentrantTestGrain());

        IReentrantGrain grain = fixture.Client.GetGrain<IReentrantGrain>("g");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(grain.SlowOp(), grain.SlowOp());
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 80,
            $"Expected concurrent execution (~50ms), got {sw.ElapsedMilliseconds}ms");

        await fixture.DisposeAsync();
    }

    // --- grain definitions ---

    public interface ISerialGrain : IGrain, IGrainWithStringKey
    {
        Task SlowAppend(string value);
    }

    public sealed class SerialGrain : Grain, ISerialGrain
    {
        public async Task SlowAppend(string value)
        {
            await Task.Delay(50);
        }
    }

    public interface IReentrantGrain : IGrain, IGrainWithStringKey
    {
        Task SlowOp();
    }

    [Reentrant]
    public sealed class ReentrantTestGrain : Grain, IReentrantGrain
    {
        public async Task SlowOp()
        {
            await Task.Delay(50);
        }
    }

    // Minimal hand-written proxies for test grains
    private sealed class SerialGrainProxy(GrainId id, IGrainCallInvoker invoker)
        : GrainProxyBase(id, invoker), ISerialGrain
    {
        public Task SlowAppend(string value) => InvokeVoidAsync(1, [value]);
    }

    private sealed class ReentrantTestGrainProxy(GrainId id, IGrainCallInvoker invoker)
        : GrainProxyBase(id, invoker), IReentrantGrain
    {
        public Task SlowOp() => InvokeVoidAsync(1, null);
    }

    private sealed class SerialGrainMethodInvoker : IGrainMethodInvoker
    {
        public Task<object?> Invoke(Grain grain, uint methodId, object?[]? args)
        {
            var g = (SerialGrain)grain;
            return methodId switch
            {
                1 => g.SlowAppend((string)args![0]!).ContinueWith<object?>(_ => null),
                _ => throw new NotImplementedException()
            };
        }
    }

    private sealed class ReentrantGrainMethodInvoker : IGrainMethodInvoker
    {
        public Task<object?> Invoke(Grain grain, uint methodId, object?[]? args)
        {
            var g = (ReentrantTestGrain)grain;
            return methodId switch
            {
                1 => g.SlowOp().ContinueWith<object?>(_ => null),
                _ => throw new NotImplementedException()
            };
        }
    }
}
```

> Note: `SimpleGrainFixture<TInterface, TGrain>` and `GrainProxyBase` are helper types you need to add in the next steps. `IGrainCallInvoker` is already in `Quark.Core.Abstractions`.

- [ ] **Step 2.2 — Create `ReentrantAttribute.cs`**

Create `src/Quark.Core.Abstractions/Grains/ReentrantAttribute.cs`:

```csharp
namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Marks a grain class as reentrant, allowing concurrent interleaved execution
///     while an awaited call is in progress.
///     Drop-in equivalent of Orleans' <c>[Reentrant]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ReentrantAttribute : Attribute
{
}
```

- [ ] **Step 2.3 — Modify `GrainActivation.cs` to detect `[Reentrant]` and dispatch concurrently**

In `src/Quark.Runtime/GrainActivation.cs`, add a field and change `PostAsync`:

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

public sealed class GrainActivation : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _isReentrant;
    private readonly ILogger<GrainActivation> _logger;
    private readonly Task _processingLoop;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    internal GrainActivation(Grain grain, GrainContext context, ILogger<GrainActivation> logger)
    {
        _logger = logger;
        _isReentrant = grain.GetType().IsDefined(typeof(ReentrantAttribute), inherit: true);
        Grain = grain;
        Context = context;
        _processingLoop = RunLoopAsync(_cts.Token);
        context.SetScheduler(PostAsync);
    }

    public Grain Grain { get; }
    public GrainContext Context { get; }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try { await _processingLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await _cts.CancelAsync();
        _cts.Dispose();
    }

    public async ValueTask PostAsync(Func<Task> workItem)
    {
        if (_isReentrant)
        {
            // Reentrant: execute directly on caller's thread — concurrent callers
            // each run their work item without queuing.
            await workItem().ConfigureAwait(false);
            return;
        }
        await _queue.Writer.WriteAsync(workItem, _cts.Token).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (Func<Task> work in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try { await work().ConfigureAwait(false); }
            catch (Exception e) { _logger.LogError(e, "Error executing grain method on {GrainId}", Context.GrainId); }
        }
    }
}
```

- [ ] **Step 2.4 — Add `GrainProxyBase` helper for tests**

Create `tests/Quark.Tests.Unit/Integration/GrainProxyBase.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.Integration;

/// <summary>Minimal base class for hand-written test proxies.</summary>
public abstract class GrainProxyBase(GrainId grainId, IGrainCallInvoker invoker) : IGrain
{
    protected GrainId GrainId { get; } = grainId;
    protected IGrainCallInvoker Invoker { get; } = invoker;

    protected Task InvokeVoidAsync(uint methodId, object?[]? args, CancellationToken ct = default)
        => Invoker.InvokeVoidAsync(GrainId, methodId, args, ct);

    protected Task<T> InvokeAsync<T>(uint methodId, object?[]? args, CancellationToken ct = default)
        => Invoker.InvokeAsync<T>(GrainId, methodId, args, ct);
}
```

- [ ] **Step 2.5 — Add `SimpleGrainFixture<TInterface, TGrain>` helper**

Create `tests/Quark.Tests.Unit/Integration/SimpleGrainFixture.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

/// <summary>
///     Builds a minimal in-process silo+client for a single grain type —
///     lighter-weight than GrainCallFixture for unit tests.
/// </summary>
public sealed class SimpleGrainFixture<TInterface, TGrain> : IAsyncDisposable
    where TInterface : class, IGrain
    where TGrain : Grain, new()
{
    private readonly GrainActivationTable _activationTable;
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _sp;

    public SimpleGrainFixture(
        string grainTypeName,
        Func<GrainId, IGrainCallInvoker, TInterface> proxyFactory,
        Func<TGrain> grainFactory,
        Action<IGrainMethodInvokerRegistry>? registerInvoker = null)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "test";
            o.SiloName = "silo0";
        });
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<IGrainActivator>(new DelegateGrainActivator(grainFactory));
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp => sp.GetRequiredService<GrainMethodInvokerRegistry>());
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _sp = services.BuildServiceProvider();

        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType(grainTypeName), typeof(TGrain));

        if (registerInvoker is not null)
        {
            registerInvoker(_sp.GetRequiredService<GrainMethodInvokerRegistry>());
        }

        var proxyReg = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var ifaceReg = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();
        ifaceReg.Register(typeof(TInterface), new GrainType(grainTypeName));
        proxyReg.Register<TInterface, DelegateProxy<TInterface>>(
            (id, inv) => new DelegateProxy<TInterface>(proxyFactory(id, inv)));

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        var deferred = new DeferredInvoker();
        var localFactory = new LocalGrainFactory(proxyReg, ifaceReg, deferred);

        var invoker = new LocalGrainCallInvoker(
            _activationTable,
            _sp.GetRequiredService<IGrainActivator>(),
            typeRegistry,
            _sp.GetRequiredService<IGrainDirectory>(),
            _sp.GetRequiredService<IGrainMethodInvokerRegistry>(),
            localFactory,
            _sp,
            _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

        deferred.Inner = invoker;
        Client = new LocalClusterClient(new LocalGrainFactory(proxyReg, ifaceReg, invoker));
    }

    public IClusterClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }

    // Internal helpers ---

    private sealed class DelegateGrainActivator(Func<Grain> factory) : IGrainActivator
    {
        public Grain CreateInstance(GrainType grainType) => factory();
    }

    private sealed class DelegateProxy<T>(T inner) : IGrain
    {
        public T Value => inner;
    }

    private sealed class DeferredInvoker : IGrainCallInvoker
    {
        public IGrainCallInvoker? Inner { get; set; }
        public Task<object?> InvokeAsync(GrainId id, uint m, object?[]? a, CancellationToken ct) => Inner!.InvokeAsync(id, m, a, ct);
        public Task<T> InvokeAsync<T>(GrainId id, uint m, object?[]? a, CancellationToken ct) => Inner!.InvokeAsync<T>(id, m, a, ct);
        public Task InvokeVoidAsync(GrainId id, uint m, object?[]? a, CancellationToken ct) => Inner!.InvokeVoidAsync(id, m, a, ct);
    }
}
```

> Note: `SimpleGrainFixture` uses `DelegateProxy<T>` as a shim because the proxy registry is typed. In the actual test, the proxy factory is called directly by `proxyFactory(id, invoker)` so the shim wrapper is unused. Adjust the registry call to use the real proxy type if you need `GetGrain<T>` to return the correct type. For the timing test, calling `GetGrain<IReentrantGrain>` and casting works since `LocalGrainFactory.CreateProxy<T>` returns `T`.

- [ ] **Step 2.6 — Simplify test to avoid proxy registry complexity**

Replace `ReentrantTests` with a direct low-level approach that doesn't need `GetGrain`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class ReentrantTests
{
    private static GrainActivation MakeActivation(Grain grain)
    {
        var grainId = new GrainId(new GrainType("G"), "1");
        var ctx = new GrainContext(grainId, new NullGrainFactory(), new NullServiceProvider());
        return new GrainActivation(grain, ctx, NullLogger<GrainActivation>.Instance);
    }

    [Fact]
    public async Task NonReentrantGrain_SerializesDispatch()
    {
        var grain = new NonReentrantGrain();
        await using var activation = MakeActivation(grain);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(
            activation.PostAsync(async () => await Task.Delay(50)),
            activation.PostAsync(async () => await Task.Delay(50)));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected ~100ms serial, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReentrantGrain_AllowsConcurrentDispatch()
    {
        var grain = new ReentrantGrain();
        await using var activation = MakeActivation(grain);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(
            activation.PostAsync(async () => await Task.Delay(50)),
            activation.PostAsync(async () => await Task.Delay(50)));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 80,
            $"Expected ~50ms concurrent, got {sw.ElapsedMilliseconds}ms");
    }

    private sealed class NonReentrantGrain : Grain { }

    [Reentrant]
    private sealed class ReentrantGrain : Grain { }
}
```

- [ ] **Step 2.7 — Run failing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~ReentrantTests" -v minimal
```
Expected: FAIL — `ReentrantAttribute` does not exist.

- [ ] **Step 2.8 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~ReentrantTests" -v minimal
```
Expected: 2 tests PASS.

- [ ] **Step 2.9 — Run full suite**

```bash
dotnet test Quark.slnx -v minimal
```
Expected: all tests pass.

- [ ] **Step 2.10 — Commit**

```bash
git add src/Quark.Core.Abstractions/Grains/ReentrantAttribute.cs \
        src/Quark.Runtime/GrainActivation.cs \
        tests/Quark.Tests.Unit/Grains/ReentrantTests.cs \
        tests/Quark.Tests.Unit/Integration/GrainProxyBase.cs
git commit -m "feat(F-02): add [Reentrant] attribute with concurrent dispatch in GrainActivation"
```

---

## Task 3: F-03 — Grain Timers

### Files
- Create: `src/Quark.Core.Abstractions/Timers/IGrainTimer.cs`
- Create: `src/Quark.Core.Abstractions/Timers/GrainTimerCreationOptions.cs`
- Modify: `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs`
- Modify: `src/Quark.Core.Abstractions/Grains/Grain.cs`
- Create: `src/Quark.Runtime/GrainTimer.cs`
- Modify: `src/Quark.Runtime/GrainContext.cs`
- Modify: `src/Quark.Runtime/GrainActivation.cs`
- Create: `tests/Quark.Tests.Unit/Grains/GrainTimerTests.cs`

- [ ] **Step 3.1 — Write failing tests**

Create `tests/Quark.Tests.Unit/Grains/GrainTimerTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class GrainTimerTests
{
    private static async Task<(TimerTestGrain, GrainContext, GrainActivation)> SetupAsync()
    {
        var grain = new TimerTestGrain();
        var id = new GrainId(new GrainType("TimerTestGrain"), "1");
        var ctx = new GrainContext(id, new NullGrainFactory(), new NullServiceProvider());
        var activation = new GrainActivation(grain, ctx, NullLogger<GrainActivation>.Instance);
        await ctx.ActivateAsync(grain);
        return (grain, ctx, activation);
    }

    [Fact]
    public async Task Timer_FiresAfterDueTime()
    {
        var (grain, _, activation) = await SetupAsync();

        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(30), period: Timeout.InfiniteTimeSpan);

        await Task.Delay(150);

        Assert.True(grain.FireCount >= 1, $"Expected >=1 fire, got {grain.FireCount}");
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_FiresRepeatedly()
    {
        var (grain, _, activation) = await SetupAsync();

        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(30));

        await Task.Delay(200);

        Assert.True(grain.FireCount >= 3, $"Expected >=3 fires, got {grain.FireCount}");
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_StopsWhenDisposed()
    {
        var (grain, _, activation) = await SetupAsync();

        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(20));
        await Task.Delay(100);
        int countAtDispose = grain.FireCount;
        grain.StopTimer();
        await Task.Delay(100);

        Assert.Equal(countAtDispose, grain.FireCount);
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_DisposedOnGrainDeactivation()
    {
        var (grain, ctx, activation) = await SetupAsync();

        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(20));
        await Task.Delay(80);
        int countBeforeDeactivate = grain.FireCount;
        await ctx.DeactivateAsync(grain, DeactivationReason.ApplicationRequested);
        await Task.Delay(100);

        Assert.Equal(countBeforeDeactivate, grain.FireCount);
        await activation.DisposeAsync();
    }

    // --- test grain ---

    private sealed class TimerTestGrain : Grain
    {
        private IGrainTimer? _timer;
        public int FireCount;

        public void StartTimer(TimeSpan dueTime, TimeSpan period)
        {
            _timer = RegisterGrainTimer(
                static (state, _) => { Interlocked.Increment(ref state.FireCount); return Task.CompletedTask; },
                this,
                new GrainTimerCreationOptions { DueTime = dueTime, Period = period });
        }

        public void StopTimer() => _timer?.Dispose();
    }
}
```

- [ ] **Step 3.2 — Run failing test**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GrainTimerTests" -v minimal
```
Expected: FAIL — `IGrainTimer`, `GrainTimerCreationOptions`, `RegisterGrainTimer` do not exist.

- [ ] **Step 3.3 — Create `IGrainTimer.cs`**

Create `src/Quark.Core.Abstractions/Timers/IGrainTimer.cs`:

```csharp
namespace Quark.Core.Abstractions.Timers;

/// <summary>
///     Handle for a grain-scoped timer created via <c>RegisterGrainTimer</c>.
///     Dispose to cancel. Drop-in equivalent of Orleans' <c>IGrainTimer</c>.
/// </summary>
public interface IGrainTimer : IDisposable
{
    /// <summary>Changes the timer's due time and period.</summary>
    void Change(TimeSpan dueTime, TimeSpan period);
}
```

- [ ] **Step 3.4 — Create `GrainTimerCreationOptions.cs`**

Create `src/Quark.Core.Abstractions/Timers/GrainTimerCreationOptions.cs`:

```csharp
namespace Quark.Core.Abstractions.Timers;

/// <summary>
///     Options for timers created via <c>RegisterGrainTimer</c>.
///     Drop-in equivalent of Orleans' <c>GrainTimerCreationOptions</c>.
/// </summary>
public sealed class GrainTimerCreationOptions
{
    /// <summary>Delay before the first fire. Defaults to <see cref="TimeSpan.Zero" />.</summary>
    public TimeSpan DueTime { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     Interval between subsequent fires.
    ///     Use <see cref="Timeout.InfiniteTimeSpan" /> for one-shot timers.
    /// </summary>
    public TimeSpan Period { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    ///     When <c>true</c>, the timer callback may fire even while the grain
    ///     is still executing a previous timer callback (interleaved).
    ///     When <c>false</c> (default), a pending fire is skipped if the previous one has not finished.
    /// </summary>
    public bool Interleave { get; init; } = false;
}
```

- [ ] **Step 3.5 — Add `RegisterTimer` to `IGrainContext`**

In `src/Quark.Core.Abstractions/Hosting/IGrainContext.cs`, add the method signature (add `using Quark.Core.Abstractions.Timers;` at top):

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Lifecycle;
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

    /// <summary>
    ///     Creates and registers a grain-scoped timer.
    ///     The timer is automatically disposed when the grain deactivates.
    /// </summary>
    IGrainTimer RegisterTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options);
}
```

- [ ] **Step 3.6 — Add `RegisterGrainTimer` to `Grain.cs`**

In `src/Quark.Core.Abstractions/Grains/Grain.cs`, add (add `using Quark.Core.Abstractions.Timers;` at top):

```csharp
    /// <summary>
    ///     Registers a timer that fires the given <paramref name="callback" /> on this grain's scheduler.
    ///     The timer is automatically cancelled when the grain deactivates.
    ///     Drop-in equivalent of Orleans' <c>RegisterGrainTimer</c>.
    /// </summary>
    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        return GrainContext.RegisterTimer(callback, state, options);
    }
```

- [ ] **Step 3.7 — Create `GrainTimer.cs` in runtime**

Create `src/Quark.Runtime/GrainTimer.cs`:

```csharp
using Quark.Core.Abstractions.Timers;

namespace Quark.Runtime;

internal sealed class GrainTimer<TState> : IGrainTimer
{
    private readonly Func<TState, CancellationToken, Task> _callback;
    private readonly bool _interleave;
    private readonly Func<Func<Task>, ValueTask> _post;
    private readonly TState _state;
    private readonly Timer _timer;
    private int _pending;
    private bool _disposed;

    internal GrainTimer(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options,
        Func<Func<Task>, ValueTask> postToQueue)
    {
        _callback = callback;
        _state = state;
        _interleave = options.Interleave;
        _post = postToQueue;
        _timer = new Timer(OnFire, null, options.DueTime, options.Period);
    }

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(dueTime, period);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private void OnFire(object? _)
    {
        if (_disposed) return;

        if (!_interleave && Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            return;

        _ = _post(async () =>
        {
            try
            {
                if (!_disposed) await _callback(_state, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (!_interleave) Interlocked.Exchange(ref _pending, 0);
            }
        });
    }
}
```

- [ ] **Step 3.8 — Update `GrainContext.cs` to implement `RegisterTimer` and track timers**

Replace the full content of `src/Quark.Runtime/GrainContext.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Lifecycle;
using Quark.Core.Abstractions.Timers;

namespace Quark.Runtime;

public sealed class GrainContext : IGrainContext
{
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;
    private Func<Func<Task>, ValueTask>? _scheduler;
    private readonly List<IGrainTimer> _timers = [];

    public GrainContext(GrainId grainId, IGrainFactory grainFactory, IServiceProvider serviceProvider)
    {
        GrainId = grainId;
        GrainFactory = grainFactory;
        ServiceProvider = serviceProvider;
        Lifecycle = new LifecycleSubject();
    }

    public LifecycleSubject Lifecycle { get; }
    public DeactivationReason? DeactivationReason { get; private set; }

    public GrainId GrainId { get; }
    public IGrainFactory GrainFactory { get; }
    public IServiceProvider ServiceProvider { get; }
    public ILifecycleSubject ObservableLifecycle => Lifecycle;
    public GrainActivationStatus ActivationStatus => _status;

    public void Deactivate(DeactivationReason reason)
    {
        if (_status is GrainActivationStatus.Active or GrainActivationStatus.Activating)
        {
            DeactivationReason = reason;
            _status = GrainActivationStatus.Deactivating;
            _ = StopInternalAsync(default);
        }
    }

    public IGrainTimer RegisterTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        if (_scheduler is null)
            throw new InvalidOperationException("Grain is not activated yet. Call RegisterGrainTimer from OnActivateAsync or a grain method.");

        var timer = new GrainTimer<TState>(callback, state, options, _scheduler);
        _timers.Add(timer);
        return timer;
    }

    internal void SetScheduler(Func<Func<Task>, ValueTask> scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task ActivateAsync(Grain grain, CancellationToken cancellationToken = default)
    {
        grain.SetContext(this);
        await Lifecycle.StartAsync(cancellationToken).ConfigureAwait(false);
        await grain.OnActivateAsync(cancellationToken).ConfigureAwait(false);
        _status = GrainActivationStatus.Active;
    }

    public async Task DeactivateAsync(Grain grain, DeactivationReason reason,
        CancellationToken cancellationToken = default)
    {
        _status = GrainActivationStatus.Deactivating;
        DeactivationReason = reason;
        DisposeTimers();
        await grain.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(false);
        await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        _status = GrainActivationStatus.Inactive;
    }

    private void DisposeTimers()
    {
        foreach (IGrainTimer timer in _timers) timer.Dispose();
        _timers.Clear();
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            DisposeTimers();
            await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _status = GrainActivationStatus.Inactive; }
    }
}
```

- [ ] **Step 3.9 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GrainTimerTests" -v minimal
```
Expected: 4 tests PASS.

- [ ] **Step 3.10 — Run full suite**

```bash
dotnet test Quark.slnx -v minimal
```
Expected: all tests pass.

- [ ] **Step 3.11 — Commit**

```bash
git add src/Quark.Core.Abstractions/Timers/ \
        src/Quark.Core.Abstractions/Hosting/IGrainContext.cs \
        src/Quark.Core.Abstractions/Grains/Grain.cs \
        src/Quark.Runtime/GrainTimer.cs \
        src/Quark.Runtime/GrainContext.cs \
        src/Quark.Runtime/GrainActivation.cs \
        tests/Quark.Tests.Unit/Grains/GrainTimerTests.cs
git commit -m "feat(F-03): add grain timers (RegisterGrainTimer, IGrainTimer, GrainTimerCreationOptions)"
```

---

## Task 4: F-11 — OpenTelemetry / `AddActivityPropagation()`

### Files
- Modify: `src/Quark.Runtime/LocalGrainCallInvoker.cs`
- Modify: `src/Quark.Core/Hosting/SiloBuilderExtensions.cs`
- Create: `tests/Quark.Tests.Unit/Diagnostics/ActivityPropagationTests.cs`

- [ ] **Step 4.1 — Write failing test**

Create `tests/Quark.Tests.Unit/Diagnostics/ActivityPropagationTests.cs`:

```csharp
using System.Diagnostics;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Diagnostics;

public sealed class ActivityPropagationTests : IAsyncDisposable
{
    private readonly GrainCallFixture _fixture = new();

    [Fact]
    public async Task GrainCall_CreatesActivity_WithExpectedTags()
    {
        Activity? captured = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Quark.Runtime",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => captured = a
        };
        ActivitySource.AddActivityListener(listener);

        ICounterGrain grain = _fixture.Client.GetGrain<ICounterGrain>("propagation-test");
        await grain.IncrementAsync();

        Assert.NotNull(captured);
        Assert.Equal("grain.invoke", captured!.OperationName);
        Assert.Contains(captured.Tags, t => t.Key == "grain.type");
        Assert.Contains(captured.Tags, t => t.Key == "grain.key" && t.Value == "propagation-test");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
```

- [ ] **Step 4.2 — Run failing test**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~ActivityPropagationTests" -v minimal
```
Expected: FAIL — no `Activity` is created during grain calls.

- [ ] **Step 4.3 — Add `ActivitySource` to `LocalGrainCallInvoker`**

In `src/Quark.Runtime/LocalGrainCallInvoker.cs`, add the static source field and wrap `InvokeAsync`:

```csharp
// Add at the top of the class body:
private static readonly ActivitySource QuarkActivity = new("Quark.Runtime", "1.0.0");

// Replace the public InvokeAsync(GrainId, uint, ...) method body:
public async Task<object?> InvokeAsync(
    GrainId grainId,
    uint methodId,
    object?[]? arguments = null,
    CancellationToken cancellationToken = default)
{
    using Activity? activity = QuarkActivity.StartActivity("grain.invoke");
    activity?.SetTag("grain.type", grainId.Type.Value);
    activity?.SetTag("grain.key", grainId.Key);
    activity?.SetTag("grain.method_id", methodId);

    GrainActivation activation = await GetOrActivateAsync(grainId, cancellationToken).ConfigureAwait(false);
    var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

    await activation.PostAsync(async () =>
    {
        try
        {
            IGrainMethodInvoker invoker = _methodInvokerRegistry.GetInvoker(activation.Grain.GetType());
            object? result = await invoker.Invoke(activation.Grain, methodId, arguments).ConfigureAwait(false);
            tcs.TrySetResult(result);
        }
        catch (Exception ex) { tcs.TrySetException(ex); }
    }).ConfigureAwait(false);

    return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 4.4 — Add `AddActivityPropagation()` to `SiloBuilderExtensions.cs`**

In `src/Quark.Core/Hosting/SiloBuilderExtensions.cs`:

```csharp
using Quark.Core.Abstractions.Hosting;

namespace Quark.Core.Hosting;

public static class SiloBuilderExtensions
{
    public static ISiloBuilder UseLocalhostClustering(
        this ISiloBuilder builder,
        int siloPort = 11111,
        int gatewayPort = 30000,
        string clusterId = "dev",
        string serviceId = "QuarkService")
    {
        return builder;
    }

    /// <summary>
    ///     Enables OpenTelemetry <see cref="System.Diagnostics.Activity" /> propagation
    ///     across grain calls. The Quark ActivitySource name is <c>"Quark.Runtime"</c>.
    ///     Drop-in equivalent of Orleans' <c>AddActivityPropagation()</c>.
    /// </summary>
    public static ISiloBuilder AddActivityPropagation(this ISiloBuilder builder)
    {
        // Instrumentation is always-on in LocalGrainCallInvoker via ActivitySource;
        // ActivitySource.StartActivity returns null when no listener is attached,
        // so there is no overhead when tracing is disabled.
        // This method exists as a drop-in API marker and can wire future filter options.
        return builder;
    }
}
```

- [ ] **Step 4.5 — Run passing tests**

```bash
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~ActivityPropagationTests" -v minimal
```
Expected: 1 test PASS.

- [ ] **Step 4.6 — Run full suite**

```bash
dotnet test Quark.slnx -v minimal
```
Expected: all tests pass.

- [ ] **Step 4.7 — Commit**

```bash
git add src/Quark.Runtime/LocalGrainCallInvoker.cs \
        src/Quark.Core/Hosting/SiloBuilderExtensions.cs \
        tests/Quark.Tests.Unit/Diagnostics/ActivityPropagationTests.cs
git commit -m "feat(F-11): add ActivitySource instrumentation and AddActivityPropagation() API"
```

---

## Task 5: Tick FEATURES.md

- [ ] **Step 5.1 — Mark Phase 1 items complete in `FEATURES.md`**

In `FEATURES.md`, change the Phase 1 checkboxes:

```markdown
## Phase 1 — Low-hanging fruit

- [x] **F-01** `GetPrimaryKeyString()` / `GetPrimaryKey()` / `GetPrimaryKeyLong()` helpers on `Grain` base — _Complexity: S_
- [x] **F-02** `[Reentrant]` attribute + concurrent dispatch in `GrainActivation` — _Complexity: S–M_
- [x] **F-03** Grain Timers (`RegisterGrainTimer`, `IGrainTimer`, `GrainTimerCreationOptions`) — _Complexity: M_
- [x] **F-11** `AddActivityPropagation()` / OpenTelemetry span propagation — _Complexity: S_
```

- [ ] **Step 5.2 — Commit**

```bash
git add FEATURES.md
git commit -m "docs: mark Phase 1 features complete in FEATURES.md"
```
