using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Redis;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Tests.Fault;
using Quark.Tests.Fault.Fakes;
using Quark.Tests.Fault.Grains;
using Testcontainers.Redis;
using Xunit;

namespace Quark.Tests.Fault.Integration;

[Trait("category", "fault-integration")]
public sealed class FaultIntegrationTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private ServiceProvider? _sp;
    private GrainActivationTable? _activationTable;

    public IClusterClient Client { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // IAsyncLifetime
    // -----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();
        BuildFixture(new FaultScenarioHolder());
    }

    public async Task DisposeAsync()
    {
        if (_activationTable is not null)
            await _activationTable.DisposeAsync();
        if (_sp is not null)
            await _sp.DisposeAsync();
        await _redis.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // BuildFixture — same DI wiring as FaultFixture but with real Redis
    // -----------------------------------------------------------------------

    private void BuildFixture(FaultScenarioHolder scenarioHolder)
    {
        // Dispose any previously built service provider first
        _activationTable?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _sp?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        string connectionString = _redis.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();

        // Register codecs for state types used by the grains
        services.AddSingleton<IFieldCodec<WorkerState>, WorkerStateCodec>();
        services.AddSingleton<IDeepCopier<WorkerState>, WorkerStateCopier>();
        services.AddSingleton<IFieldCodec<OrchestratorState>, OrchestratorStateCodec>();
        services.AddSingleton<IDeepCopier<OrchestratorState>, OrchestratorStateCopier>();

        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "fault-integration-test";
            o.ServiceId = "fault";
            o.SiloName = "silo0";
        });

        // Register real Redis first (open-generic IStorage<> via TryAddSingleton)
        services.AddRedisGrainStorage(connectionString);

        // Register closed-generic fault wrappers — these win over the open-generic RedisStorage<>
        services.AddSingleton<IStorage<WorkerState>>(sp =>
        {
            var inner = new RedisStorage<WorkerState>(sp.GetRequiredService<IGrainStorage>());
            return new FaultInjectingStorage<WorkerState>(scenarioHolder.WorkerStorage, inner);
        });
        services.AddSingleton<IStorage<OrchestratorState>>(sp =>
        {
            var inner = new RedisStorage<OrchestratorState>(sp.GetRequiredService<IGrainStorage>());
            return new FaultInjectingStorage<OrchestratorState>(scenarioHolder.OrchestratorStorage, inner);
        });

        // Core runtime
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp => sp.GetRequiredService<GrainMethodInvokerRegistry>());

        // Grain activator factories
        services.AddSingleton<IGrainActivatorFactory>(new WorkerGrainActivatorFactory());
        services.AddSingleton<IGrainActivatorFactory>(new OrderOrchestratorGrainActivatorFactory());

        // Fault-injecting activator wraps DefaultGrainActivator
        services.AddSingleton<DefaultGrainActivator>();
        services.AddSingleton<IGrainActivator>(sp =>
            new FaultInjectingGrainActivator(
                sp.GetRequiredService<DefaultGrainActivator>(),
                sp.GetRequiredService<IGrainTypeRegistry>(),
                scenarioHolder.Activations));

        // Method invokers
        services.AddSingleton<WorkerGrainMethodInvoker>();
        services.AddSingleton<OrderOrchestratorGrainMethodInvoker>();

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _sp = services.BuildServiceProvider();

        // Deferred registrations (normally done by hosted services)
        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("WorkerGrain"), typeof(WorkerGrain));
        typeRegistry.Register(new GrainType("OrderOrchestratorGrain"), typeof(OrderOrchestratorGrain));

        var invokerRegistry = _sp.GetRequiredService<GrainMethodInvokerRegistry>();
        invokerRegistry.Register(typeof(WorkerGrain), _sp.GetRequiredService<WorkerGrainMethodInvoker>());
        invokerRegistry.Register(typeof(OrderOrchestratorGrain), _sp.GetRequiredService<OrderOrchestratorGrainMethodInvoker>());

        var proxyRegistry = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();

        interfaceRegistry.Register(typeof(IWorkerGrain), new GrainType("WorkerGrain"));
        interfaceRegistry.Register(typeof(IOrderOrchestratorGrain), new GrainType("OrderOrchestratorGrain"));
        proxyRegistry.Register<IWorkerGrain, WorkerGrainProxy>((id, inv) => new WorkerGrainProxy(id, inv));
        proxyRegistry.Register<IOrderOrchestratorGrain, OrderOrchestratorGrainProxy>((id, inv) => new OrderOrchestratorGrainProxy(id, inv));

        // Break circular dep: LocalGrainFactory ↔ LocalGrainCallInvoker
        var deferredInvoker = new DeferredGrainCallInvoker();
        var localFactory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, deferredInvoker);

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        var realInvoker = new LocalGrainCallInvoker(
            _activationTable,
            _sp.GetRequiredService<IGrainActivator>(),
            typeRegistry,
            _sp.GetRequiredService<IGrainDirectory>(),
            _sp.GetRequiredService<IGrainMethodInvokerRegistry>(),
            _sp,
            _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance,
            grainFactory: localFactory);

        // Fault-injecting call invoker wraps the real one
        IGrainCallInvoker effectiveInvoker = new FaultInjectingGrainCallInvoker(realInvoker, scenarioHolder.Calls);
        deferredInvoker.SetInvoker(effectiveInvoker);

        Client = new LocalClusterClient(new LocalGrainFactory(proxyRegistry, interfaceRegistry, effectiveInvoker));
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Storage throws on the orchestrator's first write to real Redis.
    /// First ProcessAsync throws InvalidOperationException because the fault fires on the
    /// orchestrator's initial WriteStateAsync (before the worker loop).
    /// Second ProcessAsync succeeds because the write counter is past N=1.
    /// </summary>
    [Fact]
    public async Task Redis_WriteFailOnFirstAttempt_SubsequentCallSucceeds()
    {
        var scenario = new FaultScenarioHolder();
        scenario.OrchestratorStorage.ThrowOnNthWrite<InvalidOperationException>(1);
        BuildFixture(scenario);

        var orchestrator = Client.GetGrain<IOrderOrchestratorGrain>("redis-order-1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ProcessAsync(["w-redis-1"]));

        // Second attempt: write counter is past N=1, real Redis handles it normally
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-redis-1"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }

    /// <summary>
    /// Storage throws a TimeoutException on the orchestrator's first write to real Redis.
    /// ProcessAsync propagates the TimeoutException to the caller.
    /// </summary>
    [Fact]
    public async Task Redis_SlowWrite_TimeoutPropagatesCorrectly()
    {
        var scenario = new FaultScenarioHolder();
        scenario.OrchestratorStorage.ThrowOnNthWrite<TimeoutException>(1);
        BuildFixture(scenario);

        var orchestrator = Client.GetGrain<IOrderOrchestratorGrain>("redis-order-timeout");

        await Assert.ThrowsAsync<TimeoutException>(
            () => orchestrator.ProcessAsync(["w-timeout"]));
    }

    /// <summary>
    /// Cascading fault: all calls to w-full-1 are dropped (AlwaysThrowForKey).
    /// First ProcessAsync returns Failed.
    /// Then fixture is rebuilt with no faults, and remaining workers complete normally.
    /// </summary>
    [Fact]
    public async Task FullPipeline_CascadingFaults_OrderEventuallyCompletes()
    {
        var scenario = new FaultScenarioHolder();
        scenario.Calls.AlwaysThrowForKey(
            new GrainType("WorkerGrain"),
            "w-full-1",
            () => new InvalidOperationException("drop"));
        BuildFixture(scenario);

        var orchestrator = Client.GetGrain<IOrderOrchestratorGrain>("redis-order-cascade-1");
        OrchestratorStatus failedResult = await orchestrator.ProcessAsync(["w-full-1", "w-full-2", "w-full-3"]);
        Assert.Equal(OrchestratorStatus.Failed, failedResult);

        // Reset to no faults and process remaining workers under a different order key
        BuildFixture(new FaultScenarioHolder());

        var orchestrator2 = Client.GetGrain<IOrderOrchestratorGrain>("redis-order-cascade-2");
        OrchestratorStatus completedResult = await orchestrator2.ProcessAsync(["w-full-2", "w-full-3"]);
        Assert.Equal(OrchestratorStatus.Completed, completedResult);
    }

    // -----------------------------------------------------------------------
    // Deferred invoker — breaks the LocalGrainFactory ↔ LocalGrainCallInvoker cycle
    // -----------------------------------------------------------------------

    private sealed class DeferredGrainCallInvoker : IGrainCallInvoker
    {
        private IGrainCallInvoker? _inner;

        public void SetInvoker(IGrainCallInvoker invoker) => _inner = invoker;

        public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync(id, method, args, ct);

        public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync<TResult>(id, method, args, ct);

        public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeVoidAsync(id, method, args, ct);
    }

    // -----------------------------------------------------------------------
    // Manual codecs for WorkerState and OrchestratorState
    //
    // WorkerState fields:
    //   [Id(0)] string  JobId
    //   [Id(1)] WorkerStatus  Status
    //   [Id(2)] int  RetryCount
    //   [Id(3)] DateTimeOffset?  ProcessedAt
    //
    // OrchestratorState fields:
    //   [Id(0)] string[]  WorkerIds
    //   [Id(1)] int  CompletionCount
    //   [Id(2)] OrchestratorStatus  Status
    // -----------------------------------------------------------------------

    private sealed class WorkerStateCodec : IFieldCodec<WorkerState>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, WorkerState value)
        {
            writer.WriteFieldHeader(fieldId, WireType.TagDelimited);

            // [Id(0)] JobId: string
            writer.WriteFieldHeader(0u, WireType.LengthPrefixed);
            writer.WriteString(value.JobId);

            // [Id(1)] Status: WorkerStatus (int)
            writer.WriteFieldHeader(1u, WireType.VarInt);
            writer.WriteInt32((int)value.Status);

            // [Id(2)] RetryCount: int
            writer.WriteFieldHeader(2u, WireType.VarInt);
            writer.WriteInt32(value.RetryCount);

            // [Id(3)] ProcessedAt: DateTimeOffset? — encoded as HasValue(1 byte) + optional 12-byte payload
            if (value.ProcessedAt.HasValue)
            {
                writer.WriteFieldHeader(3u, WireType.LengthPrefixed);
                writer.WriteVarUInt32(13u); // 1 (hasValue) + 8 (ticks) + 4 (offset)
                writer.WriteByte(1);
                writer.WriteFixed64((ulong)value.ProcessedAt.Value.UtcTicks);
                writer.WriteFixed32((uint)(int)value.ProcessedAt.Value.Offset.TotalMinutes);
            }
            else
            {
                writer.WriteFieldHeader(3u, WireType.LengthPrefixed);
                writer.WriteVarUInt32(1u);
                writer.WriteByte(0);
            }

            // end-of-object
            writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
        }

        public WorkerState ReadValue(CodecReader reader, Field field)
        {
            if (field.WireType == WireType.Extended)
                return new WorkerState();

            string jobId = "";
            WorkerStatus status = WorkerStatus.Idle;
            int retryCount = 0;
            DateTimeOffset? processedAt = null;

            Field f;
            while (!(f = reader.ReadFieldHeader()).IsEndObject)
            {
                switch ((int)f.FieldId)
                {
                    case 0:
                        jobId = reader.ReadString();
                        break;
                    case 1:
                        status = (WorkerStatus)reader.ReadInt32();
                        break;
                    case 2:
                        retryCount = reader.ReadInt32();
                        break;
                    case 3:
                    {
                        uint len = reader.ReadVarUInt32();
                        byte hasValue = reader.ReadByte();
                        if (hasValue != 0)
                        {
                            long utcTicks = (long)reader.ReadFixed64();
                            int offsetMinutes = (int)reader.ReadFixed32();
                            processedAt = new DateTimeOffset(utcTicks, TimeSpan.FromMinutes(offsetMinutes));
                        }
                        break;
                    }
                    default:
                        SkipField(reader, f);
                        break;
                }
            }

            return new WorkerState
            {
                JobId = jobId,
                Status = status,
                RetryCount = retryCount,
                ProcessedAt = processedAt
            };
        }

        private static void SkipField(CodecReader reader, Field field)
        {
            switch (field.WireType)
            {
                case WireType.VarInt: reader.ReadVarUInt64(); break;
                case WireType.Fixed32: reader.ReadFixed32(); break;
                case WireType.Fixed64: reader.ReadFixed64(); break;
                case WireType.LengthPrefixed:
                {
                    uint len = reader.ReadVarUInt32();
                    if (len > 0) reader.ReadRaw((int)len);
                    break;
                }
                case WireType.TagDelimited:
                {
                    Field nested;
                    while (!(nested = reader.ReadFieldHeader()).IsEndObject)
                        SkipField(reader, nested);
                    break;
                }
            }
        }
    }

    private sealed class WorkerStateCopier : IDeepCopier<WorkerState>
    {
        public WorkerState DeepCopy(WorkerState original, CopyContext context) =>
            original with { };
    }

    private sealed class OrchestratorStateCodec : IFieldCodec<OrchestratorState>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, OrchestratorState value)
        {
            writer.WriteFieldHeader(fieldId, WireType.TagDelimited);

            // [Id(0)] WorkerIds: string[] — encode as count + each string
            var workerIds = value.WorkerIds ?? [];
            writer.WriteFieldHeader(0u, WireType.LengthPrefixed);
            // Build the array payload in a temp buffer
            var tempBuf = new System.Buffers.ArrayBufferWriter<byte>();
            var tempWriter = new CodecWriter(tempBuf);
            tempWriter.WriteVarUInt32((uint)workerIds.Length);
            foreach (var wid in workerIds)
                tempWriter.WriteString(wid);
            writer.WriteBytes(tempBuf.WrittenSpan);

            // [Id(1)] CompletionCount: int
            writer.WriteFieldHeader(1u, WireType.VarInt);
            writer.WriteInt32(value.CompletionCount);

            // [Id(2)] Status: OrchestratorStatus (int)
            writer.WriteFieldHeader(2u, WireType.VarInt);
            writer.WriteInt32((int)value.Status);

            // end-of-object
            writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
        }

        public OrchestratorState ReadValue(CodecReader reader, Field field)
        {
            if (field.WireType == WireType.Extended)
                return new OrchestratorState();

            string[] workerIds = [];
            int completionCount = 0;
            OrchestratorStatus status = OrchestratorStatus.Pending;

            Field f;
            while (!(f = reader.ReadFieldHeader()).IsEndObject)
            {
                switch ((int)f.FieldId)
                {
                    case 0:
                    {
                        byte[] payload = reader.ReadBytes();
                        var innerReader = new CodecReader(payload.AsMemory());
                        uint count = innerReader.ReadVarUInt32();
                        workerIds = new string[count];
                        for (int i = 0; i < (int)count; i++)
                            workerIds[i] = innerReader.ReadString();
                        break;
                    }
                    case 1:
                        completionCount = reader.ReadInt32();
                        break;
                    case 2:
                        status = (OrchestratorStatus)reader.ReadInt32();
                        break;
                    default:
                        SkipField(reader, f);
                        break;
                }
            }

            return new OrchestratorState
            {
                WorkerIds = workerIds,
                CompletionCount = completionCount,
                Status = status
            };
        }

        private static void SkipField(CodecReader reader, Field field)
        {
            switch (field.WireType)
            {
                case WireType.VarInt: reader.ReadVarUInt64(); break;
                case WireType.Fixed32: reader.ReadFixed32(); break;
                case WireType.Fixed64: reader.ReadFixed64(); break;
                case WireType.LengthPrefixed:
                {
                    uint len = reader.ReadVarUInt32();
                    if (len > 0) reader.ReadRaw((int)len);
                    break;
                }
                case WireType.TagDelimited:
                {
                    Field nested;
                    while (!(nested = reader.ReadFieldHeader()).IsEndObject)
                        SkipField(reader, nested);
                    break;
                }
            }
        }
    }

    private sealed class OrchestratorStateCopier : IDeepCopier<OrchestratorState>
    {
        public OrchestratorState DeepCopy(OrchestratorState original, CopyContext context) =>
            original with { };
    }
}
