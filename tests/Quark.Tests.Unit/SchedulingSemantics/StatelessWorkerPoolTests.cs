using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Placement;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Runtime.StatelessWorker;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

// --------------------------------------------------------------------------
// Grain contract
// --------------------------------------------------------------------------

public interface IStatelessWorkerTestGrain : IGrainWithStringKey
{
    Task NoOpAsync();
    Task BlockAsync();
    Task<int> IncrementCountAsync();
}

// --------------------------------------------------------------------------
// Per-worker activation state
// --------------------------------------------------------------------------

public sealed class WorkerTestState
{
    public int Count { get; set; }
}

// --------------------------------------------------------------------------
// Behavior (decorated with [StatelessWorker] so the router recognizes it)
// --------------------------------------------------------------------------

[StatelessWorker(maxLocalWorkers: 2)]
public sealed class StatelessWorkerTestBehavior : IStatelessWorkerTestGrain, Quark.Core.Abstractions.Grains.IGrainBehavior
{
    private readonly IActivationMemory<WorkerTestState> _memory;
    private readonly Gate _gate;
    private readonly EntryLog _entryLog;

    public StatelessWorkerTestBehavior(
        IActivationMemory<WorkerTestState> memory,
        Gate gate,
        EntryLog entryLog)
    {
        _memory = memory;
        _gate = gate;
        _entryLog = entryLog;
    }

    public Task NoOpAsync() => Task.CompletedTask;

    public async Task BlockAsync()
    {
        _entryLog.Record(1);
        await _gate.WaitAsync();
    }

    public async Task<int> IncrementCountAsync()
    {
        _entryLog.Record(1);
        await _gate.WaitAsync();
        return ++_memory.Value.Count;
    }
}

// --------------------------------------------------------------------------
// Behavior variant with maxLocalWorkers: 1 — the attribute value always wins over
// SiloRuntimeOptions.StatelessWorkerDefaultMaxLocalActivations (explicit beats default),
// so a test that needs exactly one worker slot must use a dedicated behavior class
// rather than trying to override the pool size via options on the maxLocalWorkers:2 type.
// --------------------------------------------------------------------------

[StatelessWorker(maxLocalWorkers: 1)]
public sealed class StatelessWorkerSingleSlotTestBehavior : IStatelessWorkerTestGrain, Quark.Core.Abstractions.Grains.IGrainBehavior
{
    private readonly Gate _gate;
    private readonly EntryLog _entryLog;

    public StatelessWorkerSingleSlotTestBehavior(Gate gate, EntryLog entryLog)
    {
        _gate = gate;
        _entryLog = entryLog;
    }

    public Task NoOpAsync() => Task.CompletedTask;

    public async Task BlockAsync()
    {
        _entryLog.Record(1);
        await _gate.WaitAsync();
    }

    public Task<int> IncrementCountAsync() => throw new NotSupportedException();
}

// --------------------------------------------------------------------------
// Hand-written invokables
// --------------------------------------------------------------------------

internal readonly struct SW_NoOpInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IStatelessWorkerTestGrain)behavior).NoOpAsync());
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct SW_BlockInvokable : IGrainVoidInvokable
{
    public uint MethodId => 1u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IStatelessWorkerTestGrain)behavior).BlockAsync());
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct SW_IncrementInvokable : IGrainInvokable<int>
{
    public uint MethodId => 2u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IStatelessWorkerTestGrain)behavior).IncrementCountAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}

// --------------------------------------------------------------------------
// Hand-written proxy
// --------------------------------------------------------------------------

public sealed class StatelessWorkerTestGrainProxy : IStatelessWorkerTestGrain, IGrainProxyActivator<StatelessWorkerTestGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public StatelessWorkerTestGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static StatelessWorkerTestGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task NoOpAsync()
        => _invoker.InvokeVoidAsync(_grainId, new SW_NoOpInvokable()).AsTask();

    public Task BlockAsync()
        => _invoker.InvokeVoidAsync(_grainId, new SW_BlockInvokable()).AsTask();

    public Task<int> IncrementCountAsync()
        => _invoker.InvokeAsync<SW_IncrementInvokable, int>(_grainId, new SW_IncrementInvokable()).AsTask();
}

// --------------------------------------------------------------------------
// Test fixture
// --------------------------------------------------------------------------

internal sealed class StatelessWorkerFixture : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public StatelessWorkerFixture(Action<SiloRuntimeOptions>? configureOptions = null, Type? behaviorType = null)
    {
        behaviorType ??= typeof(StatelessWorkerTestBehavior);
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "sw-pool-test";
            o.SiloName = "silo0";
            configureOptions?.Invoke(o);
        });

        services.AddSingleton<LifecycleSubject>();
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();

        services.AddScoped<ActivationShellAccessor>();
        services.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        services.AddScoped<CallContext>();
        services.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<IBehaviorResolver, BehaviorResolver>();

        services.AddScoped<IActivationMemory<WorkerTestState>>(sp =>
            new ActivationMemoryAccessor<WorkerTestState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<WorkerTestState>()));
        services.AddTransient(behaviorType);

        Gate = new Gate();
        services.AddSingleton(Gate);
        EntryLog = new EntryLog();
        services.AddSingleton(EntryLog);

        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("Not yet wired."));

        services.AddSingleton<IPlacementStrategyResolver, AttributePlacementStrategyResolver>();
        services.AddSingleton<StatelessWorkerRouter>();

        _serviceProvider = services.BuildServiceProvider();

        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("StatelessWorkerTestGrain"), behaviorType);

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(IStatelessWorkerTestGrain), new GrainType("StatelessWorkerTestGrain"));
        proxyRegistry.Register<IStatelessWorkerTestGrain, StatelessWorkerTestGrainProxy>(StatelessWorkerTestGrainProxy.Create);

        ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        Router = _serviceProvider.GetRequiredService<StatelessWorkerRouter>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

        var callInvoker = new LocalGrainCallInvoker(
            ActivationTable, typeRegistry, directory,
            _serviceProvider, siloOptions,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance,
            statelessWorkerRouter: Router);

        CallInvoker = callInvoker;
        grainFactoryRef = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(grainFactoryRef);
    }

    public IClusterClient Client { get; }
    public IGrainCallInvoker CallInvoker { get; }
    public GrainActivationTable ActivationTable { get; }
    public StatelessWorkerRouter Router { get; }
    public Gate Gate { get; }
    public EntryLog EntryLog { get; }

    public async ValueTask DisposeAsync()
    {
        await ActivationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }
}

// --------------------------------------------------------------------------
// Tests
// --------------------------------------------------------------------------

public sealed class StatelessWorkerPoolTests : IAsyncDisposable
{
    private readonly StatelessWorkerFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Test 1: MaxConcurrentExecutions cap — with MaxLocalActivations=2 (MCE=2),
    // a 3rd concurrent call to the same logical grain must wait until a slot is free.
    [Fact]
    public async Task MaxConcurrentExecutions_ThirdCallBlocksUntilSlotFrees()
    {
        _fixture.Gate.Reset();
        IStatelessWorkerTestGrain grain = _fixture.Client.GetGrain<IStatelessWorkerTestGrain>("mce-test");

        Task call1 = grain.BlockAsync();
        Task call2 = grain.BlockAsync();

        // Wait until both workers have entered the behavior.
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Length >= 2);

        // The 3rd call must wait at the pool gate (both slots busy).
        Task call3 = grain.BlockAsync();
        await Task.Delay(50);
        Assert.False(call3.IsCompleted, "3rd call must wait while 2 workers are in-flight.");

        // Releasing the gate unblocks call1 and call2; they free their slots; call3 can then proceed.
        _fixture.Gate.Release();
        await Task.WhenAll(call1, call2, call3).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Test 2: MaxLocalActivations cap — no more than MaxLocalActivations distinct
    // worker activations are created even under burst load.
    [Fact]
    public async Task MaxLocalActivations_CapEnforced()
    {
        _fixture.Gate.Reset();
        IStatelessWorkerTestGrain grain = _fixture.Client.GetGrain<IStatelessWorkerTestGrain>("mla-test");

        var logicalId = new GrainId(new GrainType("StatelessWorkerTestGrain"), "mla-test");

        Task call1 = grain.BlockAsync();
        Task call2 = grain.BlockAsync();

        // Both workers active simultaneously.
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Length >= 2);

        int workerCount = _fixture.ActivationTable.GetActiveActivations()
            .Count(x => StatelessWorkerIdentity.TryDecode(x.GrainId, out GrainId logical, out _) && logical == logicalId);
        Assert.Equal(2, workerCount);

        // A 3rd call waits but does not create a 3rd worker activation.
        Task call3 = grain.BlockAsync();
        await Task.Delay(50);

        int workerCountAfter = _fixture.ActivationTable.GetActiveActivations()
            .Count(x => StatelessWorkerIdentity.TryDecode(x.GrainId, out GrainId logical, out _) && logical == logicalId);
        Assert.Equal(2, workerCountAfter);

        _fixture.Gate.Release();
        await Task.WhenAll(call1, call2, call3).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Test 3: Overload RejectWhenFull — when QueueCapacity=1 and both the slot and
    // the waiter queue are full, further calls throw SchedulerOverloadException.
    [Fact]
    public async Task OverloadMode_RejectWhenFull_ThrowsSchedulerOverloadException()
    {
        await using var fixture = new StatelessWorkerFixture(
            o =>
            {
                o.StatelessWorkerQueueCapacity = 1;
                o.StatelessWorkerOverloadMode = SchedulerOverloadMode.RejectWhenFull;
            },
            behaviorType: typeof(StatelessWorkerSingleSlotTestBehavior));

        fixture.Gate.Reset();
        IStatelessWorkerTestGrain grain = fixture.Client.GetGrain<IStatelessWorkerTestGrain>("reject-test");

        // Call1 enters the behavior and blocks.
        Task call1 = grain.BlockAsync();
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Length >= 1);

        // Call2 fills the waiter queue (capacity = 1).
        Task call2 = grain.BlockAsync();
        await Task.Delay(50);

        // Call3 should be rejected immediately.
        await Assert.ThrowsAsync<SchedulerOverloadException>(() => grain.BlockAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        fixture.Gate.Release();
        await Task.WhenAll(call1, call2).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Test 4: Overload Wait (default) — excess calls backpressure until a slot is
    // free; none are rejected.
    [Fact]
    public async Task OverloadMode_Wait_AllCallsCompleteWithoutException()
    {
        await using var fixture = new StatelessWorkerFixture(o =>
        {
            o.StatelessWorkerDefaultMaxLocalActivations = 1;
            o.StatelessWorkerOverloadMode = SchedulerOverloadMode.Wait;
        });

        IStatelessWorkerTestGrain grain = fixture.Client.GetGrain<IStatelessWorkerTestGrain>("wait-test");

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => grain.NoOpAsync())
            .ToList();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Test 5: Per-worker activation-memory isolation — two concurrent calls to the
    // same logical grain land on different workers and see independent IActivationMemory<T>.
    [Fact]
    public async Task PerWorkerMemory_IndependentAcrossWorkers()
    {
        _fixture.Gate.Reset();
        IStatelessWorkerTestGrain grain = _fixture.Client.GetGrain<IStatelessWorkerTestGrain>("mem-test");

        Task<int> call1 = grain.IncrementCountAsync();
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Length >= 1);

        Task<int> call2 = grain.IncrementCountAsync();
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Length >= 2);

        _fixture.Gate.Release();

        int count1 = await call1.WaitAsync(TimeSpan.FromSeconds(5));
        int count2 = await call2.WaitAsync(TimeSpan.FromSeconds(5));

        // Each worker has an independent counter starting at 0.
        // Both increments produce 1, not 2 — state is not shared.
        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
    }

    // Test 6: Idle-collect then reactivate — deactivating a worker activation
    // removes it from the pool and the table; a subsequent call re-activates cleanly.
    [Fact]
    public async Task IdleCollect_ThenReactivate_ProducesCleanActivation()
    {
        await using var fixture = new StatelessWorkerFixture();

        IStatelessWorkerTestGrain grain = fixture.Client.GetGrain<IStatelessWorkerTestGrain>("idle-test");

        // Activate W_0 via a normal call.
        await grain.NoOpAsync();

        var logicalId = new GrainId(new GrainType("StatelessWorkerTestGrain"), "idle-test");
        (GrainId workerId, GrainActivation worker0) = fixture.ActivationTable.GetActiveActivations()
            .First(x => StatelessWorkerIdentity.TryDecode(x.GrainId, out GrainId logical, out _) && logical == logicalId);

        // Simulate idle collection: request deactivation.
        worker0.Deactivate(DeactivationReason.IdleTimeout);
        await WaitUntilAsync(() => worker0.ActivationStatus == GrainActivationStatus.Inactive);

        // The worker must be removed from the table.
        Assert.False(fixture.ActivationTable.TryGetActivation(workerId, out _),
            "Worker activation must be removed after deactivation.");

        // The next call must succeed — the router reuses the same slot ordinal cleanly.
        await grain.NoOpAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.ActivationTable.GetActiveActivations()
            .Any(x => StatelessWorkerIdentity.TryDecode(x.GrainId, out GrainId logical, out _) && logical == logicalId),
            "A fresh worker activation must exist after re-activation.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }
}
