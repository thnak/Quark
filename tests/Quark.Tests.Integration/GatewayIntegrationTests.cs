using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>Serialises all GatewayIntegrationTests to prevent port-reuse races.</summary>
[CollectionDefinition("GatewayTests", DisableParallelization = true)]
public sealed class GatewayTestsCollection { }

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
                silo.Services.AddGrain<PingGrain>();
                silo.Services.AddGrain<TrackerGrain>();
                silo.Services.AddGrain<SourceGrain>();
                silo.Services.AddGrainActivatorFactory<PingGrainActivatorFactory>();
                silo.Services.AddGrainActivatorFactory<TrackerGrainActivatorFactory>();
                silo.Services.AddGrainActivatorFactory<SourceGrainActivatorFactory>();
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("PingGrain"), PingGrain_TransportDispatcher.Instance);
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("TrackerGrain"), TrackerGrain_TransportDispatcher.Instance);
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("SourceGrain"), SourceGrain_TransportDispatcher.Instance);
                silo.UseLocalhostClustering(siloPort: SiloPort, gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                silo.Services.AddTcpTransport();
                // Silo-side IGrainFactory for GrainContext (grains calling other grains).
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
    // Grain implementations
    // =========================================================================

    public sealed class PingGrain : Grain, IPingGrain
    {
        public Task<string> Ping(string message) => Task.FromResult(message);
    }

    public sealed class TrackerGrain : Grain, ITrackerGrain
    {
        private ISourceGrain? _source;

        public Task SetSource(ISourceGrain source) { _source = source; return Task.CompletedTask; }

        public Task<string> GetSourceName()
        {
            if (_source is null) throw new InvalidOperationException("No source set.");
            // The grain key is the identity — return it without a network hop.
            return Task.FromResult(((IGrainProxy)_source).GrainId.Key);
        }
    }

    public sealed class SourceGrain : Grain, ISourceGrain
    {
        // GrainId is the protected property on Grain: GrainId.Key is the string key.
        public Task<string> GetName() => Task.FromResult(GrainId.Key);
    }

    // =========================================================================
    // Activator factories
    // =========================================================================

    private sealed class PingGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(PingGrain);
        public Grain Create(GrainId id, IServiceProvider sp) => new PingGrain();
    }

    private sealed class TrackerGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(TrackerGrain);
        public Grain Create(GrainId id, IServiceProvider sp) => new TrackerGrain();
    }

    private sealed class SourceGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(SourceGrain);
        public Grain Create(GrainId id, IServiceProvider sp) => new SourceGrain();
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
        public ValueTask<string> Invoke(Grain grain) => new(((IPingGrain)grain).Ping(_message));
        public void Serialize(ref CodecWriter writer) => writer.WriteString(_message);
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
        public ValueTask Invoke(Grain grain)
            => new(((ITrackerGrain)grain).SetSource(_source));
        public void Serialize(ref CodecWriter writer)
            => writer.WriteString(((IGrainProxy)_source).GrainId.Key);
        public static TrackerGrainProxy_SetSourceInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(factory!.GetGrain<ISourceGrain>(reader.ReadString()));
    }

    private readonly struct TrackerGrainProxy_GetSourceNameInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 1u;
        public ValueTask<string> Invoke(Grain grain)
            => new(((ITrackerGrain)grain).GetSourceName());
        public void Serialize(ref CodecWriter writer) { }
        public static TrackerGrainProxy_GetSourceNameInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null) => new();
    }

    // --- SourceGrain ---

    private readonly struct SourceGrainProxy_GetNameInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 0u;
        public ValueTask<string> Invoke(Grain grain) => new(((ISourceGrain)grain).GetName());
        public void Serialize(ref CodecWriter writer) { }
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
                _grainId, new PingGrainProxy_PingInvokable(message));
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
                _grainId, new TrackerGrainProxy_SetSourceInvokable(source));
        public Task<string> GetSourceName()
            => _invoker.InvokeAsync<TrackerGrainProxy_GetSourceNameInvokable, string>(
                _grainId, new());
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
            => _invoker.InvokeAsync<SourceGrainProxy_GetNameInvokable, string>(_grainId, new());
    }

    // =========================================================================
    // Transport dispatchers (silo-side: deserialise args, invoke local grain)
    // =========================================================================

    private sealed class PingGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly PingGrain_TransportDispatcher Instance = new();

        public async Task<object?> DispatchAsync(
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
                    return await invoker.InvokeAsync<PingGrainProxy_PingInvokable, string>(
                        grainId, inv, ct);
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for PingGrain.");
        }
    }

    private sealed class TrackerGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly TrackerGrain_TransportDispatcher Instance = new();

        public async Task<object?> DispatchAsync(
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
                    await invoker.InvokeVoidAsync<TrackerGrainProxy_SetSourceInvokable>(
                        grainId, inv, ct);
                    return null;
                }
                case 1u:
                {
                    return await invoker.InvokeAsync<TrackerGrainProxy_GetSourceNameInvokable, string>(
                        grainId, new(), ct);
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for TrackerGrain.");
        }
    }

    private sealed class SourceGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly SourceGrain_TransportDispatcher Instance = new();

        public async Task<object?> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                    return await invoker.InvokeAsync<SourceGrainProxy_GetNameInvokable, string>(
                        grainId, new(), ct);
            }
            throw new InvalidOperationException($"Unknown method {methodId} for SourceGrain.");
        }
    }
}
