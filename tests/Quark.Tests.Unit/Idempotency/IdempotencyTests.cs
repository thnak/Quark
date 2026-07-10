using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quark.Core;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Idempotency;

public sealed class IdempotencyTests
{
    // ── QuarkRequestContext ────────────────────────────────────────────────

    [Fact]
    public void WithIdempotencyKey_SetsAmbientKey()
    {
        Assert.Null(QuarkRequestContext.IdempotencyKey);
        using (QuarkRequestContext.WithIdempotencyKey("pay-42"))
        {
            Assert.Equal("pay-42", QuarkRequestContext.IdempotencyKey);
        }
        Assert.Null(QuarkRequestContext.IdempotencyKey);
    }

    [Fact]
    public void WithIdempotencyKey_Dispose_RestoresPreviousValue()
    {
        using var outer = QuarkRequestContext.WithIdempotencyKey("outer");
        Assert.Equal("outer", QuarkRequestContext.IdempotencyKey);
        using (QuarkRequestContext.WithIdempotencyKey("inner"))
        {
            Assert.Equal("inner", QuarkRequestContext.IdempotencyKey);
        }
        Assert.Equal("outer", QuarkRequestContext.IdempotencyKey);
    }

    // ── InMemoryRequestDedupStore ──────────────────────────────────────────

    private static InMemoryRequestDedupStore BuildStore(
        TimeSpan? window = null, int maxPerGrain = 64)
    {
        var opts = new IdempotencyOptions
        {
            Window = window ?? TimeSpan.FromMinutes(5),
            MaxEntriesPerGrain = maxPerGrain
        };
        return new InMemoryRequestDedupStore(Options.Create(opts));
    }

    private static GrainId MakeGrainId(string key = "g1") =>
        new(new GrainType("TestGrain"), key);

    [Fact]
    public async Task TryBeginAsync_FirstCall_ReturnsExecute()
    {
        var store = BuildStore();
        DedupLease lease = await store.TryBeginAsync(MakeGrainId(), "key1", argHash: 0);
        Assert.Equal(DedupOutcome.Execute, lease.Outcome);
    }

    [Fact]
    public async Task TryBeginAsync_AfterComplete_ReturnsReplay()
    {
        var store = BuildStore();
        var grainId = MakeGrainId();
        const string key = "key2";
        byte[] responseBytes = [1, 2, 3];

        DedupLease first = await store.TryBeginAsync(grainId, key, argHash: 42);
        Assert.Equal(DedupOutcome.Execute, first.Outcome);

        await store.CompleteAsync(grainId, key, responseBytes);

        DedupLease second = await store.TryBeginAsync(grainId, key, argHash: 42);
        Assert.Equal(DedupOutcome.Replay, second.Outcome);
        Assert.Equal(responseBytes, second.RecordedResponse.ToArray());
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyDifferentArgHash_ReturnsConflict()
    {
        var store = BuildStore();
        var grainId = MakeGrainId();
        const string key = "key3";

        await store.TryBeginAsync(grainId, key, argHash: 100);
        await store.CompleteAsync(grainId, key, ReadOnlyMemory<byte>.Empty);

        DedupLease conflict = await store.TryBeginAsync(grainId, key, argHash: 999);
        Assert.Equal(DedupOutcome.Conflict, conflict.Outcome);
    }

    [Fact]
    public async Task EvictGrain_RemovesEntries_AllowsReExecute()
    {
        var store = BuildStore();
        var grainId = MakeGrainId();
        const string key = "key4";
        byte[] responseBytes = [9, 8, 7];

        await store.TryBeginAsync(grainId, key, argHash: 1);
        await store.CompleteAsync(grainId, key, responseBytes);

        store.EvictGrain(grainId);

        DedupLease after = await store.TryBeginAsync(grainId, key, argHash: 1);
        Assert.Equal(DedupOutcome.Execute, after.Outcome);
    }

    [Fact]
    public async Task TryBeginAsync_ExpiredEntry_AllowsReExecute()
    {
        var store = BuildStore(window: TimeSpan.FromMilliseconds(1));
        var grainId = MakeGrainId();
        const string key = "key5";

        await store.TryBeginAsync(grainId, key, argHash: 7);
        await store.CompleteAsync(grainId, key, ReadOnlyMemory<byte>.Empty);

        await Task.Delay(50); // let the 1ms window expire

        DedupLease after = await store.TryBeginAsync(grainId, key, argHash: 7);
        Assert.Equal(DedupOutcome.Execute, after.Outcome);
    }

    [Fact]
    public async Task TryBeginAsync_MaxEntriesExceeded_DoesNotThrow()
    {
        var store = BuildStore(maxPerGrain: 2);
        var grainId = MakeGrainId();

        await store.TryBeginAsync(grainId, "k1", argHash: 1);
        await store.CompleteAsync(grainId, "k1", ReadOnlyMemory<byte>.Empty);

        await store.TryBeginAsync(grainId, "k2", argHash: 2);
        await store.CompleteAsync(grainId, "k2", ReadOnlyMemory<byte>.Empty);

        // Third entry must succeed even though cap is 2 (oldest evicted).
        DedupLease third = await store.TryBeginAsync(grainId, "k3", argHash: 3);
        Assert.Equal(DedupOutcome.Execute, third.Outcome);
    }

    [Fact]
    public async Task ConcurrentDuplicates_SecondAwaitsSameResult()
    {
        var store = BuildStore();
        var grainId = MakeGrainId();
        const string key = "key-concurrent";
        byte[] result = [0xAB, 0xCD];

        DedupLease first = await store.TryBeginAsync(grainId, key, argHash: 55);
        Assert.Equal(DedupOutcome.Execute, first.Outcome);

        // Second call arrives before Complete — it should await the in-flight entry.
        Task<DedupLease> secondTask = store.TryBeginAsync(grainId, key, argHash: 55).AsTask();
        Assert.False(secondTask.IsCompleted, "Second call should be awaiting the in-flight entry.");

        await store.CompleteAsync(grainId, key, result);

        DedupLease second = await secondTask;
        Assert.Equal(DedupOutcome.Replay, second.Outcome);
        Assert.Equal(result, second.RecordedResponse.ToArray());
    }

    // ── DurableRequestDedupStore (issue #163) ─────────────────────────────

    private static ServiceProvider BuildStorageProvider()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddSingleton<IFieldCodec<DurableDedupRecord>, DurableDedupRecordCodec>();
        services.AddSingleton<IDeepCopier<DurableDedupRecord>, DurableDedupRecordCopier>();
        services.AddSingleton<IGrainStorage, InMemoryGrainStorage>();
        return services.BuildServiceProvider();
    }

    private static DurableRequestDedupStore MakeDurableStore(IGrainStorage storage, TimeSpan? window = null) =>
        new(storage, Options.Create(new IdempotencyOptions { Window = window ?? TimeSpan.FromMinutes(5) }));

    [Fact]
    public async Task Durable_TryBeginAsync_FirstCall_ReturnsExecute()
    {
        using ServiceProvider sp = BuildStorageProvider();
        DurableRequestDedupStore store = MakeDurableStore(sp.GetRequiredService<IGrainStorage>());

        DedupLease lease = await store.TryBeginAsync(MakeGrainId(), "dkey1", argHash: 0);
        Assert.Equal(DedupOutcome.Execute, lease.Outcome);
    }

    [Fact]
    public async Task Durable_TryBeginAsync_AfterComplete_ReturnsReplay()
    {
        using ServiceProvider sp = BuildStorageProvider();
        DurableRequestDedupStore store = MakeDurableStore(sp.GetRequiredService<IGrainStorage>());
        var grainId = MakeGrainId();
        const string key = "dkey2";
        byte[] responseBytes = [1, 2, 3];

        DedupLease first = await store.TryBeginAsync(grainId, key, argHash: 42);
        Assert.Equal(DedupOutcome.Execute, first.Outcome);

        await store.CompleteAsync(grainId, key, responseBytes);

        DedupLease second = await store.TryBeginAsync(grainId, key, argHash: 42);
        Assert.Equal(DedupOutcome.Replay, second.Outcome);
        Assert.Equal(responseBytes, second.RecordedResponse.ToArray());
    }

    [Fact]
    public async Task Durable_TryBeginAsync_SameKeyDifferentArgHash_ReturnsConflict()
    {
        using ServiceProvider sp = BuildStorageProvider();
        DurableRequestDedupStore store = MakeDurableStore(sp.GetRequiredService<IGrainStorage>());
        var grainId = MakeGrainId();
        const string key = "dkey3";

        await store.TryBeginAsync(grainId, key, argHash: 100);
        await store.CompleteAsync(grainId, key, ReadOnlyMemory<byte>.Empty);

        DedupLease conflict = await store.TryBeginAsync(grainId, key, argHash: 999);
        Assert.Equal(DedupOutcome.Conflict, conflict.Outcome);
    }

    [Fact]
    public async Task Durable_CompletedEntry_ReplaysAcrossNewStoreInstance_OverSameStorage()
    {
        // Proves the point of the durable tier: unlike InMemoryRequestDedupStore, a completed entry
        // survives a fresh store instance (i.e. a grain deactivation + reactivation) as long as the
        // backing IGrainStorage persists.
        using ServiceProvider sp = BuildStorageProvider();
        IGrainStorage storage = sp.GetRequiredService<IGrainStorage>();
        var grainId = MakeGrainId();
        const string key = "dkey-reactivate";
        byte[] responseBytes = [7, 7, 7];

        DurableRequestDedupStore firstActivation = MakeDurableStore(storage);
        await firstActivation.TryBeginAsync(grainId, key, argHash: 55);
        await firstActivation.CompleteAsync(grainId, key, responseBytes);

        // New store instance, same underlying storage — simulates the grain reactivating.
        DurableRequestDedupStore secondActivation = MakeDurableStore(storage);
        DedupLease replay = await secondActivation.TryBeginAsync(grainId, key, argHash: 55);

        Assert.Equal(DedupOutcome.Replay, replay.Outcome);
        Assert.Equal(responseBytes, replay.RecordedResponse.ToArray());
    }

    [Fact]
    public async Task Durable_EvictGrain_DropsLocalCache_ButDurableRecordStillReplays()
    {
        // EvictGrain must not delete the durable record — only InMemoryRequestDedupStore's eviction
        // is destructive. The durable tier's whole point is surviving the deactivation that triggers it.
        using ServiceProvider sp = BuildStorageProvider();
        DurableRequestDedupStore store = MakeDurableStore(sp.GetRequiredService<IGrainStorage>());
        var grainId = MakeGrainId();
        const string key = "dkey-evict";
        byte[] responseBytes = [3, 1, 4];

        await store.TryBeginAsync(grainId, key, argHash: 9);
        await store.CompleteAsync(grainId, key, responseBytes);

        store.EvictGrain(grainId);

        DedupLease afterEvict = await store.TryBeginAsync(grainId, key, argHash: 9);
        Assert.Equal(DedupOutcome.Replay, afterEvict.Outcome);
        Assert.Equal(responseBytes, afterEvict.RecordedResponse.ToArray());
    }

    [Fact]
    public async Task Durable_ExpiredEntry_AllowsReExecute()
    {
        using ServiceProvider sp = BuildStorageProvider();
        IGrainStorage storage = sp.GetRequiredService<IGrainStorage>();
        var grainId = MakeGrainId();
        const string key = "dkey-expire";

        DurableRequestDedupStore store = MakeDurableStore(storage, window: TimeSpan.FromMilliseconds(1));
        await store.TryBeginAsync(grainId, key, argHash: 7);
        await store.CompleteAsync(grainId, key, ReadOnlyMemory<byte>.Empty);

        await Task.Delay(50); // let the 1ms window expire

        // Fresh instance so the (already-expired) local cache entry can't short-circuit the storage check.
        DurableRequestDedupStore reactivated = MakeDurableStore(storage, window: TimeSpan.FromMilliseconds(1));
        DedupLease after = await reactivated.TryBeginAsync(grainId, key, argHash: 7);
        Assert.Equal(DedupOutcome.Execute, after.Outcome);
    }

    [Fact]
    public async Task Durable_ConcurrentDuplicates_SecondAwaitsSameResult()
    {
        using ServiceProvider sp = BuildStorageProvider();
        DurableRequestDedupStore store = MakeDurableStore(sp.GetRequiredService<IGrainStorage>());
        var grainId = MakeGrainId();
        const string key = "dkey-concurrent";
        byte[] result = [0xAB, 0xCD];

        DedupLease first = await store.TryBeginAsync(grainId, key, argHash: 55);
        Assert.Equal(DedupOutcome.Execute, first.Outcome);

        Task<DedupLease> secondTask = store.TryBeginAsync(grainId, key, argHash: 55).AsTask();
        Assert.False(secondTask.IsCompleted, "Second call should be awaiting the in-flight entry.");

        await store.CompleteAsync(grainId, key, result);

        DedupLease second = await secondTask;
        Assert.Equal(DedupOutcome.Replay, second.Outcome);
        Assert.Equal(result, second.RecordedResponse.ToArray());
    }

    // ── MessageDispatcher dedup checkpoint ────────────────────────────────

    [Fact]
    public async Task Dispatch_WithKey_ExecutesOnlyOnce_SecondCallReplays()
    {
        int execCount = 0;
        var invoker = new CountingInvoker(() => execCount++);
        var (dispatcher, serializer) = BuildDispatcher(invoker);

        var grainId = new GrainId(new GrainType("TestGrain"), "d1");
        byte[] payload = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, 1u, ReadOnlyMemory<byte>.Empty));

        var headers = new MessageHeaders();
        headers.Set(QuarkHeaders.IdempotencyKey, "idem-1");

        MessageEnvelope? r1 = await dispatcher.DispatchAsync(Envelope(payload, headers, 1));
        MessageEnvelope? r2 = await dispatcher.DispatchAsync(Envelope(payload, headers, 2));

        Assert.Equal(1, execCount);
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1!.Payload.ToArray(), r2!.Payload.ToArray());
    }

    [Fact]
    public async Task Dispatch_SameKeyDifferentArgs_ReturnsConflictError()
    {
        int execCount = 0;
        var invoker = new CountingInvoker(() => execCount++);
        var (dispatcher, serializer) = BuildDispatcher(invoker);

        var grainId = new GrainId(new GrainType("TestGrain"), "d2");

        var headers = new MessageHeaders();
        headers.Set(QuarkHeaders.IdempotencyKey, "idem-2");

        byte[] args1 = GrainMessageSerializer.SerializeArgs("hello").ToArray();
        byte[] args2 = GrainMessageSerializer.SerializeArgs("world").ToArray();

        byte[] p1 = serializer.SerializeRequest(new GrainInvocationRequest(grainId, 1u, args1));
        byte[] p2 = serializer.SerializeRequest(new GrainInvocationRequest(grainId, 1u, args2));

        await dispatcher.DispatchAsync(Envelope(p1, headers, 1));
        MessageEnvelope? conflictReply = await dispatcher.DispatchAsync(Envelope(p2, headers, 2));

        Assert.Equal(1, execCount);
        Assert.NotNull(conflictReply);

        GrainInvocationResponse resp = serializer.DeserializeResponse(conflictReply!.Payload);
        Assert.False(resp.Success);
        Assert.Contains("IdempotencyKeyConflict", resp.Error);
    }

    [Fact]
    public async Task Dispatch_WithoutKey_NoDedup_ExecutesEveryTime()
    {
        int execCount = 0;
        var invoker = new CountingInvoker(() => execCount++);
        var (dispatcher, serializer) = BuildDispatcher(invoker);

        var grainId = new GrainId(new GrainType("TestGrain"), "d3");
        byte[] payload = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, 1u, ReadOnlyMemory<byte>.Empty));

        await dispatcher.DispatchAsync(Envelope(payload, null, 1));
        await dispatcher.DispatchAsync(Envelope(payload, null, 2));

        Assert.Equal(2, execCount);
    }

    [Fact]
    public async Task Dispatch_FailureOutcome_IsReplayed()
    {
        int execCount = 0;
        var invoker = new FailingInvoker("boom", () => execCount++);
        var (dispatcher, serializer) = BuildDispatcher(invoker);

        var grainId = new GrainId(new GrainType("TestGrain"), "d4");
        byte[] payload = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, 1u, ReadOnlyMemory<byte>.Empty));

        var headers = new MessageHeaders();
        headers.Set(QuarkHeaders.IdempotencyKey, "idem-fail");

        MessageEnvelope? r1 = await dispatcher.DispatchAsync(Envelope(payload, headers, 1));
        MessageEnvelope? r2 = await dispatcher.DispatchAsync(Envelope(payload, headers, 2));

        Assert.Equal(1, execCount);
        Assert.NotNull(r1);
        Assert.NotNull(r2);

        GrainInvocationResponse resp1 = serializer.DeserializeResponse(r1!.Payload);
        GrainInvocationResponse resp2 = serializer.DeserializeResponse(r2!.Payload);
        Assert.False(resp1.Success);
        Assert.False(resp2.Success);
        Assert.Equal(resp1.Error, resp2.Error);
    }

    [Fact]
    public async Task Dispatch_TransactionalCall_SkipsDedup_ExecutesTwice()
    {
        int execCount = 0;
        var invoker = new CountingInvoker(() => execCount++);
        var (dispatcher, serializer) = BuildDispatcher(invoker);

        var grainId = new GrainId(new GrainType("TestGrain"), "d5");
        byte[] payload = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, 1u, ReadOnlyMemory<byte>.Empty));

        var headers = new MessageHeaders();
        headers.Set(QuarkHeaders.IdempotencyKey, "idem-tx");
        headers.Set(QuarkHeaders.Transaction, "tx-1");

        await dispatcher.DispatchAsync(Envelope(payload, headers, 1));
        await dispatcher.DispatchAsync(Envelope(payload, headers, 2));

        // Transactional calls must never be deduped — both executions must occur.
        Assert.Equal(2, execCount);
    }

    // ── AddIdempotentCalls() DI wrapper (issue #155) ──────────────────────
    // The checkpoint logic above is exercised via hand-built stores/dispatchers;
    // these verify the actual public entry point wires the same pieces together.

    [Fact]
    public void AddIdempotentCalls_RegistersInMemoryRequestDedupStore()
    {
        using ServiceProvider sp = BuildSiloWithIdempotency();

        IRequestDedupStore store = sp.GetRequiredService<IRequestDedupStore>();
        Assert.IsType<InMemoryRequestDedupStore>(store);
    }

    [Fact]
    public void AddIdempotentCalls_NonPositiveWindow_FailsValidationOnResolve()
    {
        using ServiceProvider sp = BuildSiloWithIdempotency(o => o.Window = TimeSpan.Zero);

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value);
    }

    [Fact]
    public void AddIdempotentCalls_DurableWithoutProviderName_FailsValidationOnResolve()
    {
        using ServiceProvider sp = BuildSiloWithIdempotency(o => o.Durability = DedupDurability.Durable);

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value);
    }

    [Fact]
    public async Task AddIdempotentCalls_WiresRealMessageDispatcher_SecondCallReplays()
    {
        int execCount = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IGrainCallInvoker>(new CountingInvoker(() => execCount++));
        services.AddQuarkSilo(silo =>
        {
            silo.Services.AddLogging();
            silo.Services.AddQuarkRuntime();
            silo.AddIdempotentCalls();
        });
        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<TransportGrainDispatcherRegistry>()
            .Register(new GrainType("TestGrain"), new PassThroughDispatcher());

        IMessageDispatcher dispatcher = sp.GetRequiredService<IMessageDispatcher>();
        GrainMessageSerializer serializer = sp.GetRequiredService<GrainMessageSerializer>();

        var grainId = new GrainId(new GrainType("TestGrain"), "e2e");
        byte[] payload = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, 1u, ReadOnlyMemory<byte>.Empty));

        var headers = new MessageHeaders();
        headers.Set(QuarkHeaders.IdempotencyKey, "idem-e2e");

        MessageEnvelope? r1 = await dispatcher.DispatchAsync(Envelope(payload, headers, 1));
        MessageEnvelope? r2 = await dispatcher.DispatchAsync(Envelope(payload, headers, 2));

        Assert.Equal(1, execCount);
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1!.Payload.ToArray(), r2!.Payload.ToArray());
    }

    [Fact]
    public void AddIdempotentCalls_Durable_RegistersDurableRequestDedupStore_OverNamedProvider()
    {
        var services = new ServiceCollection();
        services.AddQuarkSilo(silo =>
        {
            silo.Services.AddLogging();
            silo.Services.AddQuarkRuntime();
            silo.Services.AddInMemoryGrainStorage("dedupStore");
            silo.AddIdempotentCalls(o =>
            {
                o.Durability = DedupDurability.Durable;
                o.DurableProviderName = "dedupStore";
            });
        });
        using ServiceProvider sp = services.BuildServiceProvider();

        IRequestDedupStore store = sp.GetRequiredService<IRequestDedupStore>();
        Assert.IsType<DurableRequestDedupStore>(store);
    }

    [Fact]
    public void AddIdempotentCalls_Durable_MissingNamedProvider_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddQuarkSilo(silo =>
        {
            silo.Services.AddLogging();
            silo.Services.AddQuarkRuntime();
            // Durable configured, but "dedupStore" was never registered via AddInMemoryGrainStorage(name).
            silo.AddIdempotentCalls(o =>
            {
                o.Durability = DedupDurability.Durable;
                o.DurableProviderName = "dedupStore";
            });
        });
        using ServiceProvider sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IRequestDedupStore>());
    }

    private static ServiceProvider BuildSiloWithIdempotency(Action<IdempotencyOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddQuarkSilo(silo =>
        {
            silo.Services.AddLogging();
            silo.Services.AddQuarkRuntime();
            silo.AddIdempotentCalls(configure);
        });
        return services.BuildServiceProvider();
    }

    // --- helpers ---

    private static MessageEnvelope Envelope(byte[] payload, MessageHeaders? headers, long correlationId) =>
        new()
        {
            CorrelationId = correlationId,
            MessageType = MessageType.Request,
            Payload = payload,
            Headers = headers
        };

    private static (MessageDispatcher dispatcher, GrainMessageSerializer serializer) BuildDispatcher(
        IGrainCallInvoker invoker)
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddQuarkRuntime();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<GrainMessageSerializer>();

        var registry = new TransportGrainDispatcherRegistry();
        registry.Register(new GrainType("TestGrain"), new PassThroughDispatcher());

        var store = BuildStore();

        var dispatcher = new MessageDispatcher(
            registry,
            invoker,
            serializer,
            grainFactory: null,
            terminalInvoker: null,
            dedupStore: store);

        return (dispatcher, serializer);
    }

    private sealed class PassThroughDispatcher : ITransportGrainDispatcher
    {
        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory, CancellationToken cancellationToken = default)
        {
            await invoker.InvokeVoidAsync(grainId, new NoOpInvokable(), cancellationToken)
                .ConfigureAwait(false);
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private readonly struct NoOpInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 1u;
        public ValueTask Invoke(IGrainBehavior behavior) => ValueTask.CompletedTask;
        public void Serialize(ref CodecWriter writer) { }
    }

    private sealed class CountingInvoker(Action onInvoke) : IGrainCallInvoker
    {
        public ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainInvokable<TResult>
        {
            onInvoke();
            return ValueTask.FromResult(default(TResult)!);
        }

        public ValueTask InvokeVoidAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainVoidInvokable
        {
            onInvoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask InvokeObserverAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IObserverVoidInvokable
        {
            onInvoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingInvoker(string errorMessage, Action onInvoke) : IGrainCallInvoker
    {
        public ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainInvokable<TResult>
        {
            onInvoke();
            throw new InvalidOperationException(errorMessage);
        }

        public ValueTask InvokeVoidAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainVoidInvokable
        {
            onInvoke();
            throw new InvalidOperationException(errorMessage);
        }

        public ValueTask InvokeObserverAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IObserverVoidInvokable
        {
            onInvoke();
            throw new InvalidOperationException(errorMessage);
        }
    }
}
