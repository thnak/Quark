# Concurrent Stream Fan-out + Serialize-Once Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the in-memory stream provider fan out to subscribers concurrently and serialize a published item once per publish (shared across all gateway/TCP subscribers), without changing any observable delivery semantics.

**Architecture:** Add two tiny AOT-safe primitives to `Quark.Streaming.Abstractions` — a `SharedStreamItem` holder that memoizes encoded bytes, and an additive `ISharedEncodingStreamObserver` interface. `GatewayClientSubscription` implements the interface so N gateway subscribers on one stream serialize once. `StreamSubscriptionRegistry` replaces its four sequential `foreach`+`await` fan-out loops with a `Task.WhenAll`-based helper that preserves backpressure and the `AggregateException` contract.

**Tech Stack:** .NET 10, C# (Native-AOT-first), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-10-stream-fanout-concurrent-serialize-once-design.md`
**Issue:** #145

## Global Constraints

- Target framework .NET 10 (`net10.0`); SDK pinned to `10.0.201` in `global.json`.
- Do **not** add `Version=` attributes to `<PackageReference>` — versions are centralized in `Directory.Packages.props`.
- Every production package has `IsTrimmable=true` + `EnableAotAnalyzer=true`. New code must not introduce runtime reflection; any unavoidable dynamic path must sit behind `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` and a `RuntimeFeature.IsDynamicCodeSupported` guard.
- `Quark.Streaming.InMemory` must stay codec-agnostic — it must not reference any serialization codec type.
- Preserve observable semantics exactly: publisher awaits all deliveries (backpressure); all failures surface as a single `AggregateException`; each subscriber receives exactly one call per publish.
- House style: file-scoped namespaces, `sealed` classes, XML doc comments on public members, collection expressions (`[..list]`) as used in the surrounding file.

---

### Task 1: Shared-encoding primitives in `Quark.Streaming.Abstractions`

**Files:**
- Create: `src/Quark.Streaming.Abstractions/SharedStreamItem.cs`
- Create: `src/Quark.Streaming.Abstractions/ISharedEncodingStreamObserver.cs`
- Test: `tests/Quark.Tests.Unit/Streaming/SharedStreamItemTests.cs`

**Interfaces:**
- Consumes: existing `IUntypedStreamObserver` (`Task OnNextAsync(object, StreamSequenceToken?)`, `Task OnErrorAsync(Exception)`, `Task OnCompletedAsync()`) and `StreamSequenceToken` — both in `Quark.Streaming.Abstractions`.
- Produces:
  - `SharedStreamItem` — `public SharedStreamItem(object item)`; `public object Item { get; }`; `public ReadOnlyMemory<byte> GetOrEncode(Func<object, ReadOnlyMemory<byte>> encode)`.
  - `ISharedEncodingStreamObserver : IUntypedStreamObserver` — `Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Quark.Tests.Unit/Streaming/SharedStreamItemTests.cs`:

```csharp
using Quark.Streaming.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class SharedStreamItemTests
{
    [Fact]
    public void GetOrEncode_InvokesEncoderOnlyOnce_AndMemoizesBytes()
    {
        var item = new SharedStreamItem("payload");
        int calls = 0;
        ReadOnlyMemory<byte> Encode(object o)
        {
            calls++;
            return new byte[] { 1, 2, 3 };
        }

        ReadOnlyMemory<byte> first = item.GetOrEncode(Encode);
        ReadOnlyMemory<byte> second = item.GetOrEncode(Encode);

        Assert.Equal(1, calls);
        Assert.True(first.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(second.Span.SequenceEqual(first.Span));
    }

    [Fact]
    public void Item_ExposesRawItem()
    {
        var payload = new object();
        var item = new SharedStreamItem(payload);
        Assert.Same(payload, item.Item);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SharedStreamItemTests"`
Expected: FAIL — compile error, `SharedStreamItem` does not exist.

- [ ] **Step 3: Create `SharedStreamItem`**

Create `src/Quark.Streaming.Abstractions/SharedStreamItem.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Carries a single published stream item through fan-out so observers that serialize the item
///     (e.g. gateway/TCP subscribers) encode it at most once and share the resulting bytes.
/// </summary>
public sealed class SharedStreamItem
{
    private ReadOnlyMemory<byte>? _encoded;

    /// <summary>Creates a holder for a single published <paramref name="item" />.</summary>
    public SharedStreamItem(object item) => Item = item;

    /// <summary>The raw published item, for observers that consume it without serialization.</summary>
    public object Item { get; }

    /// <summary>
    ///     Returns the encoded bytes, invoking <paramref name="encode" /> only on the first call and
    ///     memoizing the result for subsequent callers on the same instance.
    /// </summary>
    /// <remarks>
    ///     Fan-out invokes each observer's callback synchronously up to its first <c>await</c>, and
    ///     encoding runs in that synchronous prefix, so this executes single-threaded — no lock is
    ///     required. Even if that invariant were broken, the worst case is a redundant, deterministic
    ///     re-encode producing identical bytes; correctness is unaffected.
    /// </remarks>
    public ReadOnlyMemory<byte> GetOrEncode(Func<object, ReadOnlyMemory<byte>> encode)
        => _encoded ??= encode(Item);
}
```

- [ ] **Step 4: Create `ISharedEncodingStreamObserver`**

Create `src/Quark.Streaming.Abstractions/ISharedEncodingStreamObserver.cs`:

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>
///     Optional extension of <see cref="IUntypedStreamObserver" /> for observers that serialize the
///     stream item. Fan-out routes through <see cref="OnNextSharedAsync" /> so encoding is shared
///     across all such observers subscribed to the same stream.
/// </summary>
public interface ISharedEncodingStreamObserver : IUntypedStreamObserver
{
    /// <summary>
    ///     Delivers <paramref name="item" />, encoding it at most once across all shared-encoding
    ///     observers via <see cref="SharedStreamItem.GetOrEncode" />.
    /// </summary>
    Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SharedStreamItemTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Streaming.Abstractions/SharedStreamItem.cs \
        src/Quark.Streaming.Abstractions/ISharedEncodingStreamObserver.cs \
        tests/Quark.Tests.Unit/Streaming/SharedStreamItemTests.cs
git commit -m "feat(streaming): add SharedStreamItem + ISharedEncodingStreamObserver (#145)"
```

---

### Task 2: `GatewayClientSubscription` implements `ISharedEncodingStreamObserver`

**Files:**
- Modify: `src/Quark.Runtime/GatewayClientSubscription.cs`
- Test: `tests/Quark.Tests.Unit/Streaming/GatewayClientSubscriptionTests.cs`

**Interfaces:**
- Consumes: `SharedStreamItem`, `ISharedEncodingStreamObserver` (Task 1); `ICodecProvider.TryGetGeneralizedCodec(Type)`, `IGeneralizedCodec.WriteField(CodecWriter, uint, Type, object?)`, `CodecWriter(IBufferWriter<byte>)`, `CodecWriter.WriteByte(byte)` (all in `Quark.Serialization.Abstractions.*`).
- Produces: `GatewayClientSubscription` now declares `: ISharedEncodingStreamObserver`; adds `public Task OnNextSharedAsync(SharedStreamItem, StreamSequenceToken?)`; `OnNextAsync(object, StreamSequenceToken?)` delegates to it. Encoding is centralized in a private `ReadOnlyMemory<byte> Encode(object)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Quark.Tests.Unit/Streaming/GatewayClientSubscriptionTests.cs`:

```csharp
using System.Buffers;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class GatewayClientSubscriptionTests
{
    [Fact]
    public async Task OnNextSharedAsync_EncodesOnce_AcrossSubscribersSharingItem()
    {
        var codec = new CountingCodec();
        var provider = new FakeCodecProvider(codec);
        var pushes = new List<byte[]>();
        Task Push(ReadOnlyMemory<byte> bytes, StreamSequenceToken? _)
        {
            pushes.Add(bytes.ToArray());
            return Task.CompletedTask;
        }

        StreamId streamId = StreamId.Create("ns", "key");
        var sub1 = new GatewayClientSubscription(Guid.NewGuid(), streamId, provider, Push);
        var sub2 = new GatewayClientSubscription(Guid.NewGuid(), streamId, provider, Push);
        var shared = new SharedStreamItem("payload");

        await sub1.OnNextSharedAsync(shared, null);
        await sub2.OnNextSharedAsync(shared, null);

        Assert.Equal(1, codec.WriteCount);           // serialized once
        Assert.Equal(2, pushes.Count);               // pushed to both subscribers
        Assert.Equal(pushes[0], pushes[1]);          // identical bytes
        Assert.Equal(new byte[] { 0x42 }, pushes[0]);
    }

    [Fact]
    public async Task OnNextAsync_DelegatesToSharedPath_AndEncodes()
    {
        var codec = new CountingCodec();
        var provider = new FakeCodecProvider(codec);
        byte[]? pushed = null;
        Task Push(ReadOnlyMemory<byte> bytes, StreamSequenceToken? _)
        {
            pushed = bytes.ToArray();
            return Task.CompletedTask;
        }

        var sub = new GatewayClientSubscription(Guid.NewGuid(), StreamId.Create("ns", "key"), provider, Push);

        await sub.OnNextAsync("payload", null);

        Assert.Equal(1, codec.WriteCount);
        Assert.Equal(new byte[] { 0x42 }, pushed);
    }

    private sealed class CountingCodec : IGeneralizedCodec
    {
        public int WriteCount;
        public bool IsSupportedType(Type type) => true;
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, object? value)
        {
            WriteCount++;
            writer.WriteByte(0x42);
        }
        public object? ReadValue(CodecReader reader, Field field) => throw new NotSupportedException();
    }

    private sealed class FakeCodecProvider(IGeneralizedCodec codec) : ICodecProvider
    {
        public IFieldCodec<T>? TryGetCodec<T>() => null;
        public IFieldCodec<T> GetRequiredCodec<T>() => throw new NotSupportedException();
        public IGeneralizedCodec? TryGetGeneralizedCodec(Type type) => codec;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GatewayClientSubscriptionTests"`
Expected: FAIL — compile error, `GatewayClientSubscription` has no `OnNextSharedAsync`.

- [ ] **Step 3: Rewrite `GatewayClientSubscription`**

Replace the entire contents of `src/Quark.Runtime/GatewayClientSubscription.cs` with:

```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Server-side <see cref="IUntypedStreamObserver" /> that serializes each stream item
///     and pushes the encoded bytes to a remote client connection. Implements
///     <see cref="ISharedEncodingStreamObserver" /> so that when several gateway subscribers share a
///     stream, the item is serialized once and its bytes are reused across all of them.
/// </summary>
public sealed class GatewayClientSubscription : ISharedEncodingStreamObserver
{
    public Guid SubId { get; }
    public StreamId StreamId { get; }

    private readonly ICodecProvider _codecs;
    private readonly Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task> _push;

    public GatewayClientSubscription(
        Guid subId,
        StreamId streamId,
        ICodecProvider codecs,
        Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task> push)
    {
        SubId = subId;
        StreamId = streamId;
        _codecs = codecs;
        _push = push;
    }

    // IL3051/IL2046: intentional mismatch — IUntypedStreamObserver/ISharedEncodingStreamObserver are
    // non-typed escape hatches; only this gateway implementation performs dynamic type resolution.
    // Other implementations are free to avoid GetType(). Callers of the interfaces are safe.
#pragma warning disable IL3051, IL2046
    [RequiresDynamicCode(
        "Stream item codec resolution uses object.GetType() at runtime, which is not supported in Native AOT. " +
        "Use typed IAsyncObserver<T> subscriptions instead.")]
    [RequiresUnreferencedCode(
        "Stream item type may be trimmed by the linker. " +
        "Use typed IAsyncObserver<T> subscriptions for AOT-safe streaming.")]
    public Task OnNextAsync(object item, StreamSequenceToken? token)
        => OnNextSharedAsync(new SharedStreamItem(item), token);

    [RequiresDynamicCode(
        "Stream item codec resolution uses object.GetType() at runtime, which is not supported in Native AOT. " +
        "Use typed IAsyncObserver<T> subscriptions instead.")]
    [RequiresUnreferencedCode(
        "Stream item type may be trimmed by the linker. " +
        "Use typed IAsyncObserver<T> subscriptions for AOT-safe streaming.")]
    public Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token)
#pragma warning restore IL3051, IL2046
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new NotSupportedException(
                $"{nameof(GatewayClientSubscription)}.{nameof(OnNextSharedAsync)} requires dynamic code (JIT). " +
                "Use typed stream subscriptions in Native AOT contexts.");

        ReadOnlyMemory<byte> bytes = item.GetOrEncode(Encode);
        return _push(bytes, token);
    }

    [RequiresDynamicCode(
        "Stream item codec resolution uses object.GetType() at runtime, which is not supported in Native AOT.")]
    [RequiresUnreferencedCode(
        "Stream item type may be trimmed by the linker.")]
    private ReadOnlyMemory<byte> Encode(object item)
    {
        Type itemType = item.GetType();
        IGeneralizedCodec codec = _codecs.TryGetGeneralizedCodec(itemType)
                                  ?? throw new InvalidOperationException(
                                      $"No IGeneralizedCodec registered for {itemType.Name}");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        codec.WriteField(writer, 0, itemType, item);
        return buffer.WrittenMemory;
    }

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    public Task OnCompletedAsync() => Task.CompletedTask;
}
```

Notes:
- `Encode` is a private method (not a local function) carrying its own `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`, so the AOT analyzer stays satisfied; the method-group `Encode` delegate is created inside the annotated `OnNextSharedAsync`, which suppresses the warning at the delegate-creation site.
- The `RuntimeFeature.IsDynamicCodeSupported` guard now lives in `OnNextSharedAsync`; `OnNextAsync` reaches it by delegation, so both entry points remain guarded.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GatewayClientSubscriptionTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Confirm the existing gateway-table tests still pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GatewayClientSubscriptionTableTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Runtime/GatewayClientSubscription.cs \
        tests/Quark.Tests.Unit/Streaming/GatewayClientSubscriptionTests.cs
git commit -m "feat(streaming): gateway subscription shares one encoding per publish (#145)"
```

---

### Task 3: Concurrent fan-out in `StreamSubscriptionRegistry`

**Files:**
- Modify: `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs`
- Test: `tests/Quark.Tests.Unit/Streaming/StreamFanOutTests.cs`

**Interfaces:**
- Consumes: `SharedStreamItem`, `ISharedEncodingStreamObserver` (Task 1); existing `Subscription.OnNext/OnError/OnCompleted` (`Func<..., ValueTask>`), `IUntypedStreamObserver.OnNextAsync`.
- Produces: `PublishAsync`/`PublishErrorAsync`/`PublishCompletedAsync` fan out concurrently via a private `static Task FanOutAsync(List<Task>)`; the untyped `PublishAsync` path builds one `SharedStreamItem` and routes `ISharedEncodingStreamObserver` observers through `OnNextSharedAsync`. No signature changes.

- [ ] **Step 1: Write the failing tests**

Create `tests/Quark.Tests.Unit/Streaming/StreamFanOutTests.cs`:

```csharp
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class StreamFanOutTests
{
    [Fact]
    public async Task PublishAsync_FansOutConcurrently_FastSubscriberNotBlockedBySlowOne()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        var slowGate = new TaskCompletionSource();
        var fastDelivered = new TaskCompletionSource();

        // Subscribe the SLOW observer FIRST: with a sequential foreach the fast one would be blocked
        // behind it and this test would hang.
        var slow = new GateObserver<string>(_ => slowGate.Task);
        var fast = new GateObserver<string>(_ =>
        {
            fastDelivered.TrySetResult();
            return Task.CompletedTask;
        });
        registry.Subscribe(streamId, slow);
        registry.Subscribe(streamId, fast);

        Task publish = registry.PublishAsync(streamId, "x", null).AsTask();

        await fastDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // fast ran despite slow blocking
        Assert.False(publish.IsCompleted);                          // publisher still awaits slow (backpressure)

        slowGate.TrySetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));           // completes once all delivered
    }

    [Fact]
    public async Task PublishAsync_Untyped_SharesSingleEncoding_AcrossSharedEncodingObservers()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        int encodeCalls = 0;
        ReadOnlyMemory<byte> Encode(object o)
        {
            Interlocked.Increment(ref encodeCalls);
            return new byte[] { 7 };
        }

        var received = new List<SharedStreamItem>();
        registry.SubscribeUntyped(streamId, new RecordingSharedObserver(received, Encode));
        registry.SubscribeUntyped(streamId, new RecordingSharedObserver(received, Encode));

        await registry.PublishAsync(streamId, "payload", null);

        Assert.Equal(2, received.Count);
        Assert.Same(received[0], received[1]); // one SharedStreamItem shared across observers
        Assert.Equal(1, encodeCalls);          // encoded exactly once
    }

    [Fact]
    public async Task PublishAsync_Untyped_DeliversRawItemToPlainObserver_AndSharedToEncodingObserver()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        object? plainReceived = null;
        var plain = new PlainObserver(item => plainReceived = item);

        var sharedReceived = new List<SharedStreamItem>();
        var shared = new RecordingSharedObserver(sharedReceived, _ => new byte[] { 9 });

        registry.SubscribeUntyped(streamId, plain);
        registry.SubscribeUntyped(streamId, shared);

        await registry.PublishAsync(streamId, "payload", null);

        Assert.Equal("payload", plainReceived);          // plain observer receives the raw object
        Assert.Single(sharedReceived);
        Assert.Equal("payload", sharedReceived[0].Item); // shared observer receives the wrapped item
    }

    private sealed class GateObserver<T>(Func<T, Task> onNext) : IAsyncObserver<T>
    {
        public async ValueTask OnNextAsync(T item, StreamSequenceToken? token = null) => await onNext(item);
        public ValueTask OnErrorAsync(Exception ex) => ValueTask.CompletedTask;
        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSharedObserver(
        List<SharedStreamItem> received,
        Func<object, ReadOnlyMemory<byte>> encode) : ISharedEncodingStreamObserver
    {
        public Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token)
        {
            lock (received) { received.Add(item); }
            _ = item.GetOrEncode(encode);
            return Task.CompletedTask;
        }
        public Task OnNextAsync(object item, StreamSequenceToken? token)
            => OnNextSharedAsync(new SharedStreamItem(item), token);
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }

    private sealed class PlainObserver(Action<object> onNext) : IUntypedStreamObserver
    {
        public Task OnNextAsync(object item, StreamSequenceToken? token)
        {
            onNext(item);
            return Task.CompletedTask;
        }
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~StreamFanOutTests"`
Expected: FAIL — `PublishAsync_FansOutConcurrently...` hangs/times out under the current sequential loop (the fast observer never runs while slow blocks); the shared-encoding test fails because the untyped path calls `OnNextAsync(object)` rather than `OnNextSharedAsync`, so each observer wraps its own `SharedStreamItem` (`Assert.Same` fails).

- [ ] **Step 3: Add the `FanOutAsync` helper**

In `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs`, add this private helper inside the class (e.g. just above `PublishAsync`):

```csharp
private static async Task FanOutAsync(List<Task> tasks)
{
    Task all = Task.WhenAll(tasks);
    try
    {
        await all.ConfigureAwait(false);
    }
    catch when (all.Exception is not null)
    {
        // Task.WhenAll aggregates every failure into all.Exception; awaiting surfaces only the first.
        // Rethrow the flattened AggregateException to preserve the prior all-failures contract.
        throw all.Exception;
    }
}
```

- [ ] **Step 4: Convert `PublishAsync` to concurrent fan-out**

Replace the body of `PublishAsync<T>` (keep the implicit-activation block at the top unchanged) so the two delivery blocks read:

```csharp
if (_subs.TryGetValue(streamId, out List<Subscription>? list))
{
    List<Subscription> snapshot;
    lock (list)
    {
        snapshot = [..list];
    }

    var tasks = new List<Task>(snapshot.Count);
    foreach (Subscription sub in snapshot)
    {
        Task task;
        // Convert a synchronous throw into a faulted task so every subscriber is still invoked
        // and the failure is aggregated by FanOutAsync (matches the prior per-subscriber try/catch).
        try { task = sub.OnNext(item!, token).AsTask(); }
        catch (Exception ex) { task = Task.FromException(ex); }
        tasks.Add(task);
    }

    await FanOutAsync(tasks).ConfigureAwait(false);
}

if (_untyped.TryGetValue(streamId, out List<(Guid SubId, IUntypedStreamObserver Observer)>? untypedList))
{
    List<(Guid, IUntypedStreamObserver)> snapshot;
    lock (untypedList) { snapshot = [..untypedList]; }

    var shared = new SharedStreamItem(item!);
    var tasks = new List<Task>(snapshot.Count);
    foreach (var (_, obs) in snapshot)
    {
        Task task;
        try
        {
            task = obs is ISharedEncodingStreamObserver enc
                ? enc.OnNextSharedAsync(shared, token)
                : obs.OnNextAsync(item!, token);
        }
        catch (Exception ex) { task = Task.FromException(ex); }
        tasks.Add(task);
    }

    await FanOutAsync(tasks).ConfigureAwait(false);
}
```

- [ ] **Step 5: Convert `PublishErrorAsync` and `PublishCompletedAsync`**

Replace the fan-out loop in `PublishErrorAsync` (after the `snapshot` is taken) with:

```csharp
var tasks = new List<Task>(snapshot.Count);
foreach (Subscription sub in snapshot)
{
    Task task;
    try { task = sub.OnError(ex).AsTask(); }
    catch (Exception e) { task = Task.FromException(e); }
    tasks.Add(task);
}
await FanOutAsync(tasks).ConfigureAwait(false);
```

Replace the fan-out loop in `PublishCompletedAsync` with:

```csharp
var tasks = new List<Task>(snapshot.Count);
foreach (Subscription sub in snapshot)
{
    Task task;
    try { task = sub.OnCompleted().AsTask(); }
    catch (Exception ex) { task = Task.FromException(ex); }
    tasks.Add(task);
}
await FanOutAsync(tasks).ConfigureAwait(false);
```

Both methods keep their early `return` when no `_subs` entry exists. The old `List<Exception>? errors` accumulation and `throw new AggregateException(errors)` in all three methods are now removed — `FanOutAsync` provides the aggregation.

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~StreamFanOutTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the existing streaming unit tests (regression)**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~Streaming"`
Expected: PASS — including `UntypedSubscriptionTests.PublishAsync_UntypedObserverThrows_StillDeliversToRemainingSubscribers` (synchronous throw is now converted to a faulted task, so the healthy observer still receives the item and the result is a single `AggregateException`).

- [ ] **Step 8: Commit**

```bash
git add src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs \
        tests/Quark.Tests.Unit/Streaming/StreamFanOutTests.cs
git commit -m "perf(streaming): fan out stream delivery concurrently (#145)"
```

---

### Task 4: Full-suite verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Quark.slnx`
Expected: Build succeeded, **0 warnings** (no new IL2xxx/IL3xxx AOT-analyzer warnings from the changed files).

- [ ] **Step 2: Run the streaming integration tests**

Run: `dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj --filter "FullyQualifiedName~Streaming"`
Expected: PASS.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Quark.slnx`
Expected: PASS (all projects). If anything unrelated is flaky under parallel load, re-run the specific failing test in isolation to confirm it is pre-existing.

- [ ] **Step 4: (Optional) AOT smoke build**

Run: `dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true`
Expected: publish succeeds with no new trim/AOT warnings attributable to the changed files.

---

## Self-Review

**Spec coverage:**
- Concurrent fan-out (Task.WhenAll, backpressure + AggregateException preserved) → Task 3 `FanOutAsync` + Task 4 verification; concurrency proven by `StreamFanOutTests.PublishAsync_FansOutConcurrently...`.
- Serialize-once for gateway/untyped path → Task 1 (`SharedStreamItem`) + Task 2 (`GatewayClientSubscription`) + Task 3 (registry builds one `SharedStreamItem`); proven by `GatewayClientSubscriptionTests` + `StreamFanOutTests` encode-count assertions.
- Additive interface, no breaking change → Task 1 `ISharedEncodingStreamObserver : IUntypedStreamObserver`; existing `OnNextAsync` retained.
- Registry stays codec-agnostic → Task 3 constructs opaque `SharedStreamItem`, no codec reference; enforced by Global Constraints.
- All four fan-out loops covered → Task 3 Steps 4–5 (PublishAsync typed + untyped, PublishErrorAsync, PublishCompletedAsync).
- AOT safety → dynamic encode behind `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` + `RuntimeFeature.IsDynamicCodeSupported` guard (Task 2); new abstractions carry no dynamic code (Task 1); Task 4 Steps 1 & 4 verify no new warnings.
- Preserve "still delivers to remaining subscribers on throw" → Task 3 synchronous-throw-to-faulted-task guard; regression asserted in Task 3 Step 7.

**Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows complete code.

**Type consistency:** `SharedStreamItem(object)`, `Item`, `GetOrEncode(Func<object, ReadOnlyMemory<byte>>)`, `OnNextSharedAsync(SharedStreamItem, StreamSequenceToken?)`, and `FanOutAsync(List<Task>)` are used identically across Tasks 1–3. `GatewayClientSubscription` constructor signature `(Guid, StreamId, ICodecProvider, Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task>)` matches the existing type used in tests.
