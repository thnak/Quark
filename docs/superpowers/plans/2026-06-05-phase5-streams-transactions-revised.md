# Phase 5: F-07 Streams and F-08 Transactions

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement F-07 (pub/sub Streams) and F-08 (ACID Transactions), completing all Orleans feature parity items.

**Architecture:** F-07 adds `Quark.Streaming.Abstractions` (interfaces) and `Quark.Streaming.InMemory` (in-memory provider using `ConcurrentDictionary`+`AsyncLocal`). Grains access the provider via `ServiceProvider.GetRequiredKeyedService<IStreamProvider>(name)` — no helper on `Grain` base, which would create a circular dependency. F-08 adds `Quark.Transactions` — `TransactionalState<T>` maintains committed/pending copies, a `TransactionCoordinator` (with `AsyncLocal<Guid>`) registers commit/rollback callbacks, `CommitAsync` flushes to `IGrainStorage`. The copy function for rollback is a `Func<T,T>` injected at construction to stay AOT-safe (no `System.Text.Json` in library code).

**Tech Stack:** .NET 10 (libraries target `net9.0;net10.0`), xUnit, `Quark.Persistence.Abstractions`, `IGrainCallInvoker` invokable structs

**Corrections from prior plan draft:**
- `GrainProxyBase` does not exist — use `IGrainProxyActivator<TSelf>` + `IGrainCallInvoker` structs
- `cluster.Services` does not exist — use `cluster.PrimarySilo.Services`
- No `GetStreamProvider()` on `Grain` base (circular dep) — grains call `ServiceProvider.GetRequiredKeyedService<IStreamProvider>(name)` directly
- `UseTransactions()` targets `IServiceCollection` (tests use `ConfigureSiloServices = services => {...}`) — also exposed on `ISiloBuilder`
- `StreamSubscriptionRegistry` needs unsubscribe support (store subscription ID → handler)
- `[ThreadStatic]` in `TransactionCoordinator` doesn't survive `await` — use `AsyncLocal<Guid>`
- `TransactionalState` deep-copy must be AOT-safe — inject `Func<TState,TState> copyState`
- All tests need `services.AddQuarkRuntime()` in `ConfigureSiloServices` (TestSilo does not add it automatically)
- Tests need hand-written `IGrainActivatorFactory` for every grain (same pattern as `ReminderIntegrationTests`)

---

## File Map — F-07 Streams

| Action | File |
|---|---|
| Create | `src/Quark.Streaming.Abstractions/Quark.Streaming.Abstractions.csproj` |
| Create | `src/Quark.Streaming.Abstractions/StreamId.cs` |
| Create | `src/Quark.Streaming.Abstractions/StreamSequenceToken.cs` |
| Create | `src/Quark.Streaming.Abstractions/IAsyncObserver.cs` |
| Create | `src/Quark.Streaming.Abstractions/IStreamSubscriptionObserver.cs` |
| Create | `src/Quark.Streaming.Abstractions/StreamSubscriptionHandle.cs` |
| Create | `src/Quark.Streaming.Abstractions/IAsyncStream.cs` |
| Create | `src/Quark.Streaming.Abstractions/IStreamProvider.cs` |
| Create | `src/Quark.Streaming.Abstractions/ImplicitStreamSubscriptionAttribute.cs` |
| Create | `src/Quark.Streaming.InMemory/Quark.Streaming.InMemory.csproj` |
| Create | `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs` |
| Create | `src/Quark.Streaming.InMemory/InMemoryStream.cs` |
| Create | `src/Quark.Streaming.InMemory/InMemoryStreamProvider.cs` |
| Create | `src/Quark.Streaming.InMemory/InMemoryStreamingServiceCollectionExtensions.cs` |
| Modify | `Quark.slnx` — add both streaming packages to `/src/` folder |
| Modify | `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj` — add streaming refs |
| Create | `tests/Quark.Tests.Integration/StreamingIntegrationTests.cs` |

## File Map — F-08 Transactions

| Action | File |
|---|---|
| Create | `src/Quark.Transactions/Quark.Transactions.csproj` |
| Create | `src/Quark.Transactions/TransactionOption.cs` |
| Create | `src/Quark.Transactions/TransactionAttribute.cs` |
| Create | `src/Quark.Transactions/TransactionalStateAttribute.cs` |
| Create | `src/Quark.Transactions/ITransactionalState.cs` |
| Create | `src/Quark.Transactions/ITransactionCoordinator.cs` |
| Create | `src/Quark.Transactions/TransactionCoordinator.cs` |
| Create | `src/Quark.Transactions/TransactionalState.cs` |
| Create | `src/Quark.Transactions/TransactionServiceCollectionExtensions.cs` |
| Modify | `Quark.slnx` — add `Quark.Transactions` to `/src/` folder |
| Modify | `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj` — add transactions refs |
| Create | `tests/Quark.Tests.Integration/TransactionIntegrationTests.cs` |
| Modify | `FEATURES.md` — mark F-07 and F-08 complete |

---

## Task 16: F-07 — Streams

### Step 16.1 — Create `Quark.Streaming.Abstractions.csproj`

- [ ] Create `src/Quark.Streaming.Abstractions/Quark.Streaming.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Streaming.Abstractions</PackageId>
    <Description>Stream pub/sub abstractions for the Quark distributed actor framework.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Core.Abstractions\Quark.Core.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
  </ItemGroup>
</Project>
```

### Step 16.2 — Create `StreamId.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/StreamId.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>Identifies a stream by namespace + key. Drop-in equivalent of Orleans' <c>StreamId</c>.</summary>
public readonly struct StreamId : IEquatable<StreamId>
{
    private StreamId(string @namespace, string key) { Namespace = @namespace; Key = key; }

    public string Namespace { get; }
    public string Key { get; }

    public static StreamId Create(string @namespace, string key) => new(@namespace, key);
    public static StreamId Create(string @namespace, Guid key) => new(@namespace, key.ToString("N"));

    public bool Equals(StreamId other) => Namespace == other.Namespace && Key == other.Key;
    public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Namespace, Key);
    public override string ToString() => $"{Namespace}/{Key}";
}
```

### Step 16.3 — Create `StreamSequenceToken.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/StreamSequenceToken.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>Represents a position in a stream for ordered delivery.</summary>
public abstract class StreamSequenceToken : IComparable<StreamSequenceToken>
{
    public abstract int CompareTo(StreamSequenceToken? other);
    public abstract bool Newer(StreamSequenceToken other);
}

/// <summary>Simple sequential-integer token used by the in-memory provider.</summary>
public sealed class SequentialToken : StreamSequenceToken
{
    public SequentialToken(long sequenceNumber) => SequenceNumber = sequenceNumber;
    public long SequenceNumber { get; }

    public override int CompareTo(StreamSequenceToken? other)
        => other is SequentialToken st ? SequenceNumber.CompareTo(st.SequenceNumber) : 1;

    public override bool Newer(StreamSequenceToken other)
        => other is SequentialToken st && SequenceNumber > st.SequenceNumber;
}
```

### Step 16.4 — Create `IAsyncObserver.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/IAsyncObserver.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>Receives items from a stream. Drop-in equivalent of Orleans' <c>IAsyncObserver&lt;T&gt;</c>.</summary>
public interface IAsyncObserver<in T>
{
    Task OnNextAsync(T item, StreamSequenceToken? token = null);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();
}
```

### Step 16.5 — Create `IStreamSubscriptionObserver.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/IStreamSubscriptionObserver.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Implemented by grains that use <c>[ImplicitStreamSubscription]</c> to receive
///     subscription lifecycle notifications.
/// </summary>
public interface IStreamSubscriptionObserver
{
    Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory);
}

public interface IStreamSubscriptionHandleFactory
{
    StreamSubscriptionHandle<T> Create<T>(IAsyncObserver<T> observer);
}
```

### Step 16.6 — Create `StreamSubscriptionHandle.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/StreamSubscriptionHandle.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Handle for an active stream subscription.
///     Drop-in equivalent of Orleans' <c>StreamSubscriptionHandle&lt;T&gt;</c>.
/// </summary>
public abstract class StreamSubscriptionHandle<T>
{
    public abstract Guid HandleId { get; }
    public abstract StreamId StreamId { get; }
    public abstract Task UnsubscribeAsync();
    public abstract Task ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null);
}
```

### Step 16.7 — Create `IAsyncStream.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/IAsyncStream.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>A pub/sub stream. Drop-in equivalent of Orleans' <c>IAsyncStream&lt;T&gt;</c>.</summary>
public interface IAsyncStream<T>
{
    StreamId StreamId { get; }

    Task OnNextAsync(T item, StreamSequenceToken? token = null);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer);

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError = null,
        Func<Task>? onCompleted = null);

    Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles();
}
```

### Step 16.8 — Create `IStreamProvider.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/IStreamProvider.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Factory for obtaining stream instances.
///     Drop-in equivalent of Orleans' <c>IStreamProvider</c>.
/// </summary>
public interface IStreamProvider
{
    string Name { get; }
    IAsyncStream<T> GetStream<T>(StreamId streamId);
}
```

### Step 16.9 — Create `ImplicitStreamSubscriptionAttribute.cs`

- [ ] Create `src/Quark.Streaming.Abstractions/ImplicitStreamSubscriptionAttribute.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Marks a grain class as an implicit subscriber to any stream whose namespace matches
///     <see cref="StreamNamespace"/>. The grain should subscribe in <c>OnActivateAsync</c>
///     via <c>ServiceProvider.GetRequiredKeyedService&lt;IStreamProvider&gt;(name).GetStream&lt;T&gt;(streamId).SubscribeAsync(this)</c>.
///     Drop-in equivalent of Orleans' <c>[ImplicitStreamSubscription]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ImplicitStreamSubscriptionAttribute : Attribute
{
    public ImplicitStreamSubscriptionAttribute(string streamNamespace)
        => StreamNamespace = streamNamespace;

    public string StreamNamespace { get; }
}
```

### Step 16.10 — Create `Quark.Streaming.InMemory.csproj`

- [ ] Create `src/Quark.Streaming.InMemory/Quark.Streaming.InMemory.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Streaming.InMemory</PackageId>
    <Description>In-memory stream provider for the Quark distributed actor framework.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Streaming.Abstractions\Quark.Streaming.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
  </ItemGroup>
</Project>
```

### Step 16.11 — Create `StreamSubscriptionRegistry.cs`

> Note: values are `List<(Guid, Func<>)>` — locking on the list is correct because `ConcurrentDictionary.GetOrAdd` ensures all callers share the same `List` instance.

- [ ] Create `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class StreamSubscriptionRegistry
{
    private readonly ConcurrentDictionary<StreamId, List<(Guid Id, Func<object, StreamSequenceToken?, Task> Handler)>> _subs = new();

    public Guid Subscribe<T>(StreamId streamId, IAsyncObserver<T> observer)
    {
        var list = _subs.GetOrAdd(streamId, _ => []);
        var id = Guid.NewGuid();
        Func<object, StreamSequenceToken?, Task> handler = (item, token) =>
            observer.OnNextAsync((T)item, token);
        lock (list) list.Add((id, handler));
        return id;
    }

    public void Unsubscribe(StreamId streamId, Guid subscriptionId)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        lock (list) list.RemoveAll(s => s.Id == subscriptionId);
    }

    public async Task PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<(Guid, Func<object, StreamSequenceToken?, Task>)> snapshot;
        lock (list) snapshot = [..list];
        foreach (var (_, handler) in snapshot)
            await handler(item!, token).ConfigureAwait(false);
    }
}
```

### Step 16.12 — Create `InMemoryStream.cs`

- [ ] Create `src/Quark.Streaming.InMemory/InMemoryStream.cs`:

```csharp
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class InMemoryStream<T> : IAsyncStream<T>
{
    private readonly StreamSubscriptionRegistry _registry;
    private readonly List<InMemorySubscriptionHandle<T>> _handles = [];
    private long _sequence;

    public InMemoryStream(StreamId streamId, StreamSubscriptionRegistry registry)
    {
        StreamId = streamId;
        _registry = registry;
    }

    public StreamId StreamId { get; }

    public Task OnNextAsync(T item, StreamSequenceToken? token = null)
    {
        var seq = new SequentialToken(Interlocked.Increment(ref _sequence));
        return _registry.PublishAsync(StreamId, item, token ?? seq);
    }

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
    {
        var subscriptionId = _registry.Subscribe(StreamId, observer);
        var handle = new InMemorySubscriptionHandle<T>(subscriptionId, StreamId, _registry);
        lock (_handles) _handles.Add(handle);
        return Task.FromResult<StreamSubscriptionHandle<T>>(handle);
    }

    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError = null,
        Func<Task>? onCompleted = null)
        => SubscribeAsync(new DelegateObserver<T>(onNext, onError, onCompleted));

    public Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles()
    {
        lock (_handles) return Task.FromResult<IList<StreamSubscriptionHandle<T>>>([.._handles]);
    }
}

internal sealed class InMemorySubscriptionHandle<T> : StreamSubscriptionHandle<T>
{
    private readonly StreamSubscriptionRegistry _registry;

    public InMemorySubscriptionHandle(Guid id, StreamId streamId, StreamSubscriptionRegistry registry)
    {
        HandleId = id;
        StreamId = streamId;
        _registry = registry;
    }

    public override Guid HandleId { get; }
    public override StreamId StreamId { get; }

    public override Task UnsubscribeAsync()
    {
        _registry.Unsubscribe(StreamId, HandleId);
        return Task.CompletedTask;
    }

    public override Task ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null)
        => Task.CompletedTask;
}

internal sealed class DelegateObserver<T> : IAsyncObserver<T>
{
    private readonly Func<T, StreamSequenceToken?, Task> _onNext;
    private readonly Func<Exception, Task>? _onError;
    private readonly Func<Task>? _onCompleted;

    public DelegateObserver(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError,
        Func<Task>? onCompleted)
    {
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public Task OnNextAsync(T item, StreamSequenceToken? token = null) => _onNext(item, token);
    public Task OnErrorAsync(Exception ex) => _onError?.Invoke(ex) ?? Task.CompletedTask;
    public Task OnCompletedAsync() => _onCompleted?.Invoke() ?? Task.CompletedTask;
}
```

### Step 16.13 — Create `InMemoryStreamProvider.cs`

- [ ] Create `src/Quark.Streaming.InMemory/InMemoryStreamProvider.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public sealed class InMemoryStreamProvider : IStreamProvider
{
    private readonly ConcurrentDictionary<StreamId, object> _streams = new();
    private readonly StreamSubscriptionRegistry _registry = new();

    public InMemoryStreamProvider(string name) => Name = name;

    public string Name { get; }

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
        => (IAsyncStream<T>)_streams.GetOrAdd(streamId, _ => new InMemoryStream<T>(streamId, _registry));
}
```

### Step 16.14 — Create `InMemoryStreamingServiceCollectionExtensions.cs`

- [ ] Create `src/Quark.Streaming.InMemory/InMemoryStreamingServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public static class InMemoryStreamingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a named in-memory stream provider.
    ///     Drop-in equivalent of Orleans' <c>AddMemoryStreams(name)</c>.
    /// </summary>
    public static IServiceCollection AddMemoryStreams(this IServiceCollection services, string providerName)
    {
        services.AddKeyedSingleton<IStreamProvider>(providerName,
            (_, _) => new InMemoryStreamProvider(providerName));
        return services;
    }
}
```

### Step 16.15 — Add streaming packages to `Quark.slnx`

- [ ] Edit `Quark.slnx`, add two entries inside the `<Folder Name="/src/">` block (after `Quark.Server`):

```xml
        <Project Path="src/Quark.Streaming.Abstractions/Quark.Streaming.Abstractions.csproj"/>
        <Project Path="src/Quark.Streaming.InMemory/Quark.Streaming.InMemory.csproj"/>
```

### Step 16.16 — Add streaming references to integration test project

- [ ] Edit `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <ProjectReference Include="..\..\src\Quark.Streaming.Abstractions\Quark.Streaming.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Streaming.InMemory\Quark.Streaming.InMemory.csproj"/>
```

### Step 16.17 — Write streaming integration tests

- [ ] Create `tests/Quark.Tests.Integration/StreamingIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

[Trait("category", "integration")]
public sealed class StreamingIntegrationTests
{
    // -----------------------------------------------------------------------
    // Direct unit-level tests (no cluster required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExplicitSubscription_ReceivesPublishedItems()
    {
        var provider = new InMemoryStreamProvider("chat");
        var stream = provider.GetStream<string>(StreamId.Create("room", "general"));

        var received = new List<string>();
        await stream.SubscribeAsync((msg, _) => { received.Add(msg); return Task.CompletedTask; });

        await stream.OnNextAsync("hello");
        await stream.OnNextAsync("world");

        Assert.Equal(["hello", "world"], received);
    }

    [Fact]
    public async Task UnsubscribeHandle_StopsDelivery()
    {
        var provider = new InMemoryStreamProvider("chat");
        var stream = provider.GetStream<string>(StreamId.Create("room", "unsub"));

        var received = new List<string>();
        var handle = await stream.SubscribeAsync((msg, _) => { received.Add(msg); return Task.CompletedTask; });

        await stream.OnNextAsync("before");
        await handle.UnsubscribeAsync();
        await stream.OnNextAsync("after");

        Assert.Equal(["before"], received);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveMessages()
    {
        var provider = new InMemoryStreamProvider("chat");
        var stream = provider.GetStream<int>(StreamId.Create("nums", "test"));

        var a = new List<int>();
        var b = new List<int>();
        await stream.SubscribeAsync((n, _) => { a.Add(n); return Task.CompletedTask; });
        await stream.SubscribeAsync((n, _) => { b.Add(n); return Task.CompletedTask; });

        await stream.OnNextAsync(1);
        await stream.OnNextAsync(2);

        Assert.Equal([1, 2], a);
        Assert.Equal([1, 2], b);
    }

    // -----------------------------------------------------------------------
    // Grain self-subscription via TestCluster
    // Grains with [ImplicitStreamSubscription] subscribe themselves in OnActivateAsync.
    // Auto-activation on publish is deferred to a future phase.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrainSubscribesInOnActivate_ReceivesStreamMessages()
    {
        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddMemoryStreams("events");
                services.AddGrain<StreamListenerGrain>();
                services.AddGrainActivatorFactory<StreamListenerGrainActivatorFactory>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IStreamListenerGrain, StreamListenerGrainProxy>();
            };
        });

        // Calling any method activates the grain; OnActivateAsync self-subscribes to the stream.
        IStreamListenerGrain grain = cluster.Client.GetGrain<IStreamListenerGrain>("sensor1");
        await grain.GetLastValueAsync();

        // Publish via the silo-side stream provider.
        var provider = cluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("events");
        var stream = provider.GetStream<int>(StreamId.Create("readings", "sensor1"));
        await stream.OnNextAsync(42);
        await stream.OnNextAsync(99);
        await Task.Delay(50);

        Assert.Equal(99, await grain.GetLastValueAsync());
    }

    // -----------------------------------------------------------------------
    // Grain stub
    // -----------------------------------------------------------------------

    public interface IStreamListenerGrain : IGrainWithStringKey
    {
        Task<int> GetLastValueAsync();
    }

    [ImplicitStreamSubscription("readings")]
    private sealed class StreamListenerGrain : Grain, IStreamListenerGrain, IAsyncObserver<int>
    {
        private int _last;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var provider = ServiceProvider.GetRequiredKeyedService<IStreamProvider>("events");
            var streamId = StreamId.Create("readings", GetPrimaryKeyString());
            await provider.GetStream<int>(streamId).SubscribeAsync(this);
        }

        public Task<int> GetLastValueAsync() => Task.FromResult(_last);
        public Task OnNextAsync(int item, StreamSequenceToken? token = null) { _last = item; return Task.CompletedTask; }
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }

    private sealed class StreamListenerGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(StreamListenerGrain);
        public Grain Create(GrainId grainId, IServiceProvider services) => new StreamListenerGrain();
    }

    // Invokable
    private readonly struct StreamListenerGrain_GetLastValueInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 0u;
        public ValueTask<int> Invoke(Grain grain) => new(((IStreamListenerGrain)grain).GetLastValueAsync());
    }

    // Proxy
    private sealed class StreamListenerGrainProxy : IStreamListenerGrain, IGrainProxyActivator<StreamListenerGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public StreamListenerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static StreamListenerGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task<int> GetLastValueAsync()
            => _invoker.InvokeAsync<StreamListenerGrain_GetLastValueInvokable, int>(
                _grainId, new StreamListenerGrain_GetLastValueInvokable());
    }
}
```

### Step 16.18 — Build and run streaming tests

- [ ] Run:

```bash
dotnet build src/Quark.Streaming.Abstractions/Quark.Streaming.Abstractions.csproj && \
dotnet build src/Quark.Streaming.InMemory/Quark.Streaming.InMemory.csproj && \
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
    --filter "FullyQualifiedName~StreamingIntegrationTests" -v minimal
```

Expected: 4 tests PASS (3 direct + 1 TestCluster).

### Step 16.19 — Full suite + commit

- [ ] Run full suite and commit:

```bash
dotnet test Quark.slnx -v minimal
git add src/Quark.Streaming.Abstractions/ \
        src/Quark.Streaming.InMemory/ \
        tests/Quark.Tests.Integration/StreamingIntegrationTests.cs \
        tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
        Quark.slnx
git commit -m "feat(F-07): add Streams (IAsyncStream<T>, InMemoryStreamProvider, ImplicitStreamSubscription)"
```

---

## Task 17: F-08 — Transactions

### Step 17.1 — Create `Quark.Transactions.csproj`

- [ ] Create `src/Quark.Transactions/Quark.Transactions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <PackageId>Quark.Transactions</PackageId>
    <Description>ACID transaction support for the Quark distributed actor framework.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quark.Persistence.Abstractions\Quark.Persistence.Abstractions.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions"/>
  </ItemGroup>
</Project>
```

### Step 17.2 — Create `TransactionOption.cs`

- [ ] Create `src/Quark.Transactions/TransactionOption.cs`:

```csharp
namespace Quark.Transactions;

/// <summary>
///     Controls how a grain method participates in a transaction.
///     Drop-in equivalent of Orleans' <c>TransactionOption</c>.
/// </summary>
public enum TransactionOption
{
    Create,
    Join,
    CreateOrJoin,
    Supported,
    NotAllowed
}
```

### Step 17.3 — Create `TransactionAttribute.cs`

- [ ] Create `src/Quark.Transactions/TransactionAttribute.cs`:

```csharp
namespace Quark.Transactions;

/// <summary>
///     Marks a grain method as participating in a transaction.
///     Drop-in equivalent of Orleans' <c>[Transaction]</c>.
///     In Phase 5 this is metadata only; auto-coordination middleware is deferred.
///     Tests coordinate manually via <see cref="ITransactionCoordinator"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TransactionAttribute : Attribute
{
    public TransactionAttribute(TransactionOption option = TransactionOption.CreateOrJoin)
        => Option = option;

    public TransactionOption Option { get; }
}
```

### Step 17.4 — Create `TransactionalStateAttribute.cs`

- [ ] Create `src/Quark.Transactions/TransactionalStateAttribute.cs`:

```csharp
namespace Quark.Transactions;

/// <summary>
///     Marks a grain constructor parameter as a transactional state slot.
///     Drop-in equivalent of Orleans' <c>[TransactionalState]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TransactionalStateAttribute : Attribute
{
    public TransactionalStateAttribute(string stateName, string? storageName = null)
    {
        StateName = stateName;
        StorageName = storageName;
    }

    public string StateName { get; }
    public string? StorageName { get; }
}
```

### Step 17.5 — Create `ITransactionalState.cs`

- [ ] Create `src/Quark.Transactions/ITransactionalState.cs`:

```csharp
namespace Quark.Transactions;

/// <summary>
///     Provides transactional read/write access to persistent grain state.
///     Drop-in equivalent of Orleans' <c>ITransactionalState&lt;TState&gt;</c>.
/// </summary>
public interface ITransactionalState<TState> where TState : new()
{
    Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction);
    Task PerformUpdate(Action<TState> updateFunction);
    Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction);
}
```

### Step 17.6 — Create `ITransactionCoordinator.cs`

- [ ] Create `src/Quark.Transactions/ITransactionCoordinator.cs`:

```csharp
namespace Quark.Transactions;

/// <summary>
///     Manages transaction lifecycle: begin, commit, abort, 2PC coordination.
/// </summary>
public interface ITransactionCoordinator
{
    Guid BeginTransaction();
    Task CommitAsync(Guid transactionId);
    Task AbortAsync(Guid transactionId, Exception? reason = null);
    bool IsInTransaction(out Guid transactionId);
}
```

### Step 17.7 — Create `TransactionCoordinator.cs`

> Uses `AsyncLocal<Guid>` — not `[ThreadStatic]` — so transaction ID flows correctly across `await` boundaries.

- [ ] Create `src/Quark.Transactions/TransactionCoordinator.cs`:

```csharp
using System.Collections.Concurrent;

namespace Quark.Transactions;

public sealed class TransactionCoordinator : ITransactionCoordinator
{
    private static readonly AsyncLocal<Guid> _currentTransactionId = new();

    private readonly ConcurrentDictionary<Guid, TransactionContext> _transactions = new();

    public Guid BeginTransaction()
    {
        var id = Guid.NewGuid();
        _transactions[id] = new TransactionContext();
        _currentTransactionId.Value = id;
        return id;
    }

    public async Task CommitAsync(Guid transactionId)
    {
        if (!_transactions.TryRemove(transactionId, out var ctx)) return;
        foreach (var (commit, _) in ctx.Writers)
            await commit().ConfigureAwait(false);
        _currentTransactionId.Value = Guid.Empty;
    }

    public Task AbortAsync(Guid transactionId, Exception? reason = null)
    {
        if (_transactions.TryRemove(transactionId, out var ctx))
            foreach (var (_, rollback) in ctx.Writers)
                rollback();
        _currentTransactionId.Value = Guid.Empty;
        return Task.CompletedTask;
    }

    public bool IsInTransaction(out Guid transactionId)
    {
        transactionId = _currentTransactionId.Value;
        return transactionId != Guid.Empty;
    }

    internal void RegisterWriter(Guid transactionId, Func<Task> commit, Action rollback)
    {
        if (_transactions.TryGetValue(transactionId, out var ctx))
            ctx.Writers.Add((commit, rollback));
    }

    private sealed class TransactionContext
    {
        public List<(Func<Task> Commit, Action Rollback)> Writers { get; } = [];
    }
}
```

### Step 17.8 — Create `TransactionalState.cs`

> Deep copy is provided by the caller as a `Func<TState, TState>` to remain AOT-safe.
> Each `TransactionalState` registers its commit/rollback handler only **once** per transaction
> (tracked by `_registeredForTxId`). Multiple `PerformUpdate` calls within one transaction
> accumulate changes in `_pendingState`; a single commit/rollback handles all of them.

- [ ] Create `src/Quark.Transactions/TransactionalState.cs`:

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;

namespace Quark.Transactions;

public sealed class TransactionalState<TState> : ITransactionalState<TState> where TState : new()
{
    private readonly string _stateName;
    private readonly GrainId _grainId;
    private readonly IGrainStorage _storage;
    private readonly TransactionCoordinator _coordinator;
    private readonly Func<TState, TState> _copyState;

    private TState _committed = new();
    private TState? _pending;
    private Guid _registeredForTxId;

    public TransactionalState(
        string stateName,
        GrainId grainId,
        IGrainStorage storage,
        TransactionCoordinator coordinator,
        Func<TState, TState> copyState)
    {
        _stateName = stateName;
        _grainId = grainId;
        _storage = storage;
        _coordinator = coordinator;
        _copyState = copyState;
    }

    /// <summary>
    ///     Loads committed state from storage. Call from <c>OnActivateAsync</c>.
    /// </summary>
    public async Task LoadAsync()
    {
        var wrapper = new GrainState<TState>();
        await _storage.ReadStateAsync(_stateName, _grainId, wrapper).ConfigureAwait(false);
        _committed = wrapper.State;
    }

    public Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction)
        => Task.FromResult(readFunction(_pending ?? _committed));

    public Task PerformUpdate(Action<TState> updateFunction)
    {
        EnsurePending();
        updateFunction(_pending!);
        return Task.CompletedTask;
    }

    public Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction)
    {
        EnsurePending();
        return Task.FromResult(updateFunction(_pending!));
    }

    private void EnsurePending()
    {
        if (_pending == null)
            _pending = _copyState(_committed);

        if (!_coordinator.IsInTransaction(out var txId)) return;
        if (_registeredForTxId == txId) return;

        _registeredForTxId = txId;
        _coordinator.RegisterWriter(txId, CommitAsync, Rollback);
    }

    private async Task CommitAsync()
    {
        if (_pending == null) return;
        _committed = _pending;
        _pending = null;
        _registeredForTxId = Guid.Empty;
        var wrapper = new GrainState<TState> { State = _committed };
        await _storage.WriteStateAsync(_stateName, _grainId, wrapper).ConfigureAwait(false);
    }

    private void Rollback()
    {
        _pending = null;
        _registeredForTxId = Guid.Empty;
    }
}
```

### Step 17.9 — Create `TransactionServiceCollectionExtensions.cs`

- [ ] Create `src/Quark.Transactions/TransactionServiceCollectionExtensions.cs`:

> Note: `Quark.Transactions` does not reference `Quark.Core` so `ISiloBuilder` is not available here.
> Tests use `services.AddTransactions()` in `ConfigureSiloServices`. For ISiloBuilder extension,
> users can add an extension in their application layer if needed.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quark.Transactions;

public static class TransactionServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Quark transaction coordinator.
    ///     Drop-in equivalent of Orleans' <c>UseTransactions()</c>.
    /// </summary>
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.TryAddSingleton<TransactionCoordinator>();
        services.TryAddSingleton<ITransactionCoordinator>(sp =>
            sp.GetRequiredService<TransactionCoordinator>());
        return services;
    }
}
```

### Step 17.10 — Add transactions package to `Quark.slnx`

- [ ] Edit `Quark.slnx`, add one entry inside the `<Folder Name="/src/">` block:

```xml
        <Project Path="src/Quark.Transactions/Quark.Transactions.csproj"/>
```

### Step 17.11 — Add transactions references to integration test project

- [ ] Edit `tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <ProjectReference Include="..\..\src\Quark.Transactions\Quark.Transactions.csproj"/>
```

### Step 17.12 — Write transaction integration tests

> The test manually calls `BeginTransaction` / `CommitAsync` / `AbortAsync` because `[Transaction]`
> auto-wiring middleware is deferred. The grain uses `[Transaction]` as metadata only.

- [ ] Create `tests/Quark.Tests.Integration/TransactionIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Testing.Harness;
using Quark.Transactions;
using Xunit;

namespace Quark.Tests.Integration;

[Trait("category", "integration")]
public sealed class TransactionIntegrationTests
{
    private static Action<TestClusterOptions> BuildOptions() => options =>
    {
        options.ConfigureSiloServices = services =>
        {
            services.AddQuarkRuntime();
            services.AddInMemoryGrainStorage();
            services.AddTransactions();
            services.AddGrain<AccountGrain>();
            services.AddGrainActivatorFactory<AccountGrainActivatorFactory>();
        };
        options.ConfigureClientServices = services =>
        {
            services.AddLocalClusterClient();
            services.AddGrainProxy<IAccountGrain, AccountGrainProxy>();
        };
    };

    [Fact]
    public async Task Commit_PersistsBalanceChange()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice");

        Guid txId = coordinator.BeginTransaction();
        await alice.DepositAsync(100m);
        await coordinator.CommitAsync(txId);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    [Fact]
    public async Task Abort_RollsBackPendingChange()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice-abort");

        // First commit: set balance to 100
        Guid txSetup = coordinator.BeginTransaction();
        await alice.DepositAsync(100m);
        await coordinator.CommitAsync(txSetup);

        // Second tx: deposit 400 but abort — alice should stay at 100
        Guid txAbort = coordinator.BeginTransaction();
        await alice.DepositAsync(400m);
        await coordinator.AbortAsync(txAbort);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    [Fact]
    public async Task MultipleUpdates_InOneTx_CommitAppliesAll()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice-multi");

        Guid txId = coordinator.BeginTransaction();
        await alice.DepositAsync(50m);
        await alice.DepositAsync(50m);
        await coordinator.CommitAsync(txId);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    // -----------------------------------------------------------------------
    // Grain
    // -----------------------------------------------------------------------

    public interface IAccountGrain : IGrainWithStringKey
    {
        Task DepositAsync(decimal amount);
        Task<decimal> GetBalanceAsync();
    }

    private sealed class AccountGrain : Grain, IAccountGrain
    {
        private readonly ITransactionalState<Balance> _balance;

        public AccountGrain(ITransactionalState<Balance> balance) => _balance = balance;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
            => ((TransactionalState<Balance>)_balance).LoadAsync();

        [Transaction(TransactionOption.CreateOrJoin)]
        public Task DepositAsync(decimal amount) => _balance.PerformUpdate(b => b.Value += amount);

        public Task<decimal> GetBalanceAsync() => _balance.PerformRead(b => b.Value);
    }

    public sealed class Balance { public decimal Value { get; set; } }

    // Hand-written activator: injects TransactionalState with a lambda-based deep copier.
    private sealed class AccountGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(AccountGrain);

        public Grain Create(GrainId grainId, IServiceProvider services)
        {
            var storage = services.GetRequiredService<IGrainStorage>();
            var coordinator = services.GetRequiredService<TransactionCoordinator>();
            var balance = new TransactionalState<Balance>(
                "balance",
                grainId,
                storage,
                coordinator,
                src => new Balance { Value = src.Value });
            return new AccountGrain(balance);
        }
    }

    // Invokables
    private readonly struct AccountGrain_DepositInvokable : IGrainVoidInvokable
    {
        private readonly decimal _amount;
        public AccountGrain_DepositInvokable(decimal amount) => _amount = amount;
        public uint MethodId => 0u;
        public ValueTask Invoke(Grain grain) => new(((IAccountGrain)grain).DepositAsync(_amount));
    }

    private readonly struct AccountGrain_GetBalanceInvokable : IGrainInvokable<decimal>
    {
        public uint MethodId => 1u;
        public ValueTask<decimal> Invoke(Grain grain) => new(((IAccountGrain)grain).GetBalanceAsync());
    }

    // Proxy
    private sealed class AccountGrainProxy : IAccountGrain, IGrainProxyActivator<AccountGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public AccountGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static AccountGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task DepositAsync(decimal amount)
            => _invoker.InvokeVoidAsync(_grainId, new AccountGrain_DepositInvokable(amount));

        public Task<decimal> GetBalanceAsync()
            => _invoker.InvokeAsync<AccountGrain_GetBalanceInvokable, decimal>(
                _grainId, new AccountGrain_GetBalanceInvokable());
    }
}
```

### Step 17.13 — Build and run transaction tests

- [ ] Run:

```bash
dotnet build src/Quark.Transactions/Quark.Transactions.csproj && \
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
    --filter "FullyQualifiedName~TransactionIntegrationTests" -v minimal
```

Expected: 3 tests PASS.

### Step 17.14 — Full suite + commit

- [ ] Run full suite and commit:

```bash
dotnet test Quark.slnx -v minimal
git add src/Quark.Transactions/ \
        tests/Quark.Tests.Integration/TransactionIntegrationTests.cs \
        tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj \
        Quark.slnx
git commit -m "feat(F-08): add Transactions (ITransactionalState<T>, TransactionCoordinator, 2PC commit/rollback)"
```

---

## Task 18: Tick FEATURES.md

- [ ] In `FEATURES.md`, change:

```
- [ ] **F-07** Streams ...
- [ ] **F-08** Transactions ...
```

to:

```
- [x] **F-07** Streams ...
- [x] **F-08** Transactions ...
```

- [ ] Commit:

```bash
git add FEATURES.md
git commit -m "docs: mark Phase 5 complete — F-07 Streams, F-08 Transactions"
```

---

## Known limitations and future work

| Item | Deferred to |
|---|---|
| `[ImplicitStreamSubscription]` auto-activation on publish (runtime scans grain types, activates on message) | Phase 6 |
| `[Transaction]` auto-coordination middleware (wraps grain calls via interceptor) | Phase 6 |
| Cross-silo stream delivery (remote fan-out) | Phase 7+ |
| Durable/persistent stream providers (Kafka, Service Bus) | Future |
