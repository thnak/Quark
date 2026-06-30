using System.Buffers;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     End-to-end gateway tests using real loopback TCP sockets.
///     Tests spin up a silo host and a separate client host in-process.
///     Placed in a named collection to prevent parallel execution (port reuse).
/// </summary>
[Collection("GatewayTests")]
[Trait("category", "gateway")]
public sealed class GatewayIntegrationTests : IAsyncLifetime
{
    // Use an offset port to avoid conflicts with other test silos on 11111/30000.
    private const int SiloPort    = 11200;
    private const int GatewayPort = 30100;

    // Unique cluster ID per test-class instance so each test's silo gets a fresh
    // SharedLocalhostCluster entry — prevents "Membership entry already exists" errors
    // when xUnit runs multiple tests sequentially in the same process.
    private readonly string _clusterId = $"gw-test-{Guid.NewGuid():N}";

    private IHost _siloHost = null!;
    private IHost _clientHost = null!;

    public async Task InitializeAsync()
    {
        _siloHost = Host.CreateDefaultBuilder()
            .UseQuark(silo =>
            {
                silo.Services.AddQuarkRuntime();
                silo.Services.AddGrainBehavior<IPingGrain, PingGrainBehavior>();
                silo.Services.AddGrainBehavior<ITrackerGrain, TrackerGrainBehavior>();
                silo.Services.AddGrainBehavior<ISourceGrain, SourceGrainBehavior>();

                // Activation memory for each behavior
                silo.Services.AddScoped<IActivationMemory<PingGrainState>>(sp =>
                    new ActivationMemoryAccessor<PingGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<PingGrainState>()));
                silo.Services.AddScoped<IActivationMemory<TrackerGrainState>>(sp =>
                    new ActivationMemoryAccessor<TrackerGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<TrackerGrainState>()));
                silo.Services.AddScoped<IActivationMemory<SourceGrainState>>(sp =>
                    new ActivationMemoryAccessor<SourceGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<SourceGrainState>()));

                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("PingGrain"), PingGrain_TransportDispatcher.Instance);
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("TrackerGrain"), TrackerGrain_TransportDispatcher.Instance);
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("SourceGrain"), SourceGrain_TransportDispatcher.Instance);
                silo.UseLocalhostClustering(siloPort: SiloPort, gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                silo.Services.AddTcpTransport();
                // Silo-side IGrainFactory for behaviors calling other grains.
                silo.Services.AddLocalClusterClient();
                silo.Services.AddGrainProxy<IPingGrain, PingGrainProxy>();
                silo.Services.AddGrainProxy<ITrackerGrain, TrackerGrainProxy>();
                silo.Services.AddGrainProxy<ISourceGrain, SourceGrainProxy>();
            })
            .Build();

        _clientHost = Host.CreateDefaultBuilder()
            .UseQuarkClient(client =>
            {
                client.UseLocalhostGateway(GatewayPort);
                client.Services.AddGrainProxy<IPingGrain, PingGrainProxy>();
                client.Services.AddGrainProxy<ITrackerGrain, TrackerGrainProxy>();
                client.Services.AddGrainProxy<ISourceGrain, SourceGrainProxy>();
            })
            .Build();

        await _siloHost.StartAsync();
        await _clientHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _clientHost.StopAsync();
        await _siloHost.StopAsync();
        _clientHost.Dispose();
        _siloHost.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 1 — basic string round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ping_RoundTrip_ReturnsEchoedMessage()
    {
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();
        IPingGrain ping = client.GetGrain<IPingGrain>("p1");

        string result = await ping.Ping("hello");

        Assert.Equal("hello", result);
    }

    // -------------------------------------------------------------------------
    // Test 2 — grain-ref parameter round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GrainRef_Parameter_IsDeserializedCorrectly()
    {
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();
        ITrackerGrain tracker = client.GetGrain<ITrackerGrain>("tracker-1");
        ISourceGrain source  = client.GetGrain<ISourceGrain>("source-1");

        await tracker.SetSource(source);
        string name = await tracker.GetSourceName();

        Assert.Equal("source-1", name);
    }

    // =========================================================================
    // Grain interfaces
    // =========================================================================

    public interface IPingGrain : IGrainWithStringKey
    {
        Task<string> Ping(string message);
    }

    public interface ITrackerGrain : IGrainWithStringKey
    {
        Task SetSource(ISourceGrain source);
        Task<string> GetSourceName();
    }

    public interface ISourceGrain : IGrainWithStringKey
    {
        Task<string> GetName();
    }

    // =========================================================================
    // State classes
    // =========================================================================

    private sealed class PingGrainState { }

    private sealed class TrackerGrainState
    {
        public ISourceGrain? Source { get; set; }
    }

    private sealed class SourceGrainState { }

    // =========================================================================
    // Grain behavior implementations
    // =========================================================================

    private sealed class PingGrainBehavior : IGrainBehavior, IPingGrain
    {
        public PingGrainBehavior(IActivationMemory<PingGrainState> _) { }

        public Task<string> Ping(string message) => Task.FromResult(message);
    }

    private sealed class TrackerGrainBehavior : IGrainBehavior, ITrackerGrain
    {
        private readonly IActivationMemory<TrackerGrainState> _memory;

        public TrackerGrainBehavior(IActivationMemory<TrackerGrainState> memory)
        {
            _memory = memory;
        }

        public Task SetSource(ISourceGrain source) { _memory.Value.Source = source; return Task.CompletedTask; }

        public Task<string> GetSourceName()
        {
            if (_memory.Value.Source is null) throw new InvalidOperationException("No source set.");
            // The grain key is the identity — return it without a network hop.
            return Task.FromResult(((IGrainProxy)_memory.Value.Source).GrainId.Key);
        }
    }

    private sealed class SourceGrainBehavior : IGrainBehavior, ISourceGrain
    {
        private readonly ICallContext _ctx;

        public SourceGrainBehavior(ICallContext ctx, IActivationMemory<SourceGrainState> _)
        {
            _ctx = ctx;
        }

        public Task<string> GetName() => Task.FromResult(_ctx.GrainId.Key);
    }

    // =========================================================================
    // Invokable structs
    // =========================================================================

    // --- PingGrain ---

    private readonly struct PingGrainProxy_PingInvokable : IGrainInvokable<string>
    {
        private readonly string _message;
        public PingGrainProxy_PingInvokable(string message) => _message = message;
        public uint MethodId => 0u;
        public ValueTask<string> Invoke(IGrainBehavior behavior) => new(((IPingGrain)behavior).Ping(_message));
        public void Serialize(ref CodecWriter writer) => writer.WriteString(_message);
        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
        public static PingGrainProxy_PingInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(reader.ReadString());
    }

    // --- TrackerGrain ---

    private readonly struct TrackerGrainProxy_SetSourceInvokable : IGrainVoidInvokable
    {
        private readonly ISourceGrain _source;
        public TrackerGrainProxy_SetSourceInvokable(ISourceGrain source) => _source = source;
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior)
            => new(((ITrackerGrain)behavior).SetSource(_source));
        public void Serialize(ref CodecWriter writer)
            => writer.WriteString(((IGrainProxy)_source).GrainId.Key);
        public static TrackerGrainProxy_SetSourceInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(factory!.GetGrain<ISourceGrain>(reader.ReadString()));
    }

    private readonly struct TrackerGrainProxy_GetSourceNameInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 1u;
        public ValueTask<string> Invoke(IGrainBehavior behavior)
            => new(((ITrackerGrain)behavior).GetSourceName());
        public void Serialize(ref CodecWriter writer) { }
        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
        public static TrackerGrainProxy_GetSourceNameInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null) => new();
    }

    // --- SourceGrain ---

    private readonly struct SourceGrainProxy_GetNameInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 0u;
        public ValueTask<string> Invoke(IGrainBehavior behavior) => new(((ISourceGrain)behavior).GetName());
        public void Serialize(ref CodecWriter writer) { }
        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
        public static SourceGrainProxy_GetNameInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null) => new();
    }

    // =========================================================================
    // Proxy classes
    // =========================================================================

    private sealed class PingGrainProxy
        : IPingGrain, IGrainProxyActivator<PingGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;
        public PingGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }
        public GrainId GrainId => _grainId;
        public static PingGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);
        public Task<string> Ping(string message)
            => _invoker.InvokeAsync<PingGrainProxy_PingInvokable, string>(
                _grainId, new PingGrainProxy_PingInvokable(message)).AsTask();
    }

    private sealed class TrackerGrainProxy
        : ITrackerGrain, IGrainProxyActivator<TrackerGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;
        public TrackerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }
        public GrainId GrainId => _grainId;
        public static TrackerGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);
        public Task SetSource(ISourceGrain source)
            => _invoker.InvokeVoidAsync<TrackerGrainProxy_SetSourceInvokable>(
                _grainId, new TrackerGrainProxy_SetSourceInvokable(source)).AsTask();
        public Task<string> GetSourceName()
            => _invoker.InvokeAsync<TrackerGrainProxy_GetSourceNameInvokable, string>(
                _grainId, new()).AsTask();
    }

    private sealed class SourceGrainProxy
        : ISourceGrain, IGrainProxyActivator<SourceGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;
        public SourceGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }
        public GrainId GrainId => _grainId;
        public static SourceGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);
        public Task<string> GetName()
            => _invoker.InvokeAsync<SourceGrainProxy_GetNameInvokable, string>(_grainId, new()).AsTask();
    }

    // =========================================================================
    // Transport dispatchers (silo-side: deserialise args, invoke local grain)
    // =========================================================================

    private sealed class PingGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly PingGrain_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                {
                    var reader = new CodecReader(argumentPayload);
                    var inv = PingGrainProxy_PingInvokable.Deserialize(ref reader, factory);
                    string result = await invoker.InvokeAsync<PingGrainProxy_PingInvokable, string>(grainId, inv, ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var writer = new CodecWriter(buf);
                    writer.WriteString(result);
                    return buf.WrittenMemory.ToArray();
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for PingGrain.");
        }
    }

    private sealed class TrackerGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly TrackerGrain_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                {
                    var reader = new CodecReader(argumentPayload);
                    var inv = TrackerGrainProxy_SetSourceInvokable.Deserialize(ref reader, factory);
                    await invoker.InvokeVoidAsync<TrackerGrainProxy_SetSourceInvokable>(grainId, inv, ct);
                    return ReadOnlyMemory<byte>.Empty;
                }
                case 1u:
                {
                    string result = await invoker.InvokeAsync<TrackerGrainProxy_GetSourceNameInvokable, string>(grainId, new(), ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var writer = new CodecWriter(buf);
                    writer.WriteString(result);
                    return buf.WrittenMemory.ToArray();
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for TrackerGrain.");
        }
    }

    private sealed class SourceGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly SourceGrain_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                {
                    string result = await invoker.InvokeAsync<SourceGrainProxy_GetNameInvokable, string>(grainId, new(), ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var writer = new CodecWriter(buf);
                    writer.WriteString(result);
                    return buf.WrittenMemory.ToArray();
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for SourceGrain.");
        }
    }
}
