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
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     TCP cross-check for failure-semantics guarantee 1 (issue #130): a behavior-method throw
///     must reach the client with the server's exception text, over a real loopback gateway —
///     not just through the in-process <c>LocalGrainCallInvoker</c> path covered in
///     <c>Quark.Tests.Unit/FailureSemantics</c>. Verified against <c>MessageDispatcher</c>'s
///     <c>success=false</c> response shape and <c>TcpGatewayCallInvoker</c>'s client-side throw.
/// </summary>
[Collection("GatewayTests")]
[Trait("category", "gateway")]
public sealed class FailureSemanticsGatewayTests : IAsyncLifetime
{
    private const int SiloPort = 11204;
    private const int GatewayPort = 30104;

    private readonly string _clusterId = $"gw-failure-{Guid.NewGuid():N}";

    private IHost _siloHost = null!;
    private IHost _clientHost = null!;

    public async Task InitializeAsync()
    {
        _siloHost = Host.CreateDefaultBuilder()
            .UseQuark(silo =>
            {
                silo.Services.AddQuarkRuntime();
                silo.Services.AddGrainBehavior<IThrowingGrain, ThrowingGrainBehavior>();
                silo.Services.AddScoped<IActivationMemory<ThrowingGrainState>>(sp =>
                    new ActivationMemoryAccessor<ThrowingGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<ThrowingGrainState>()));
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("ThrowingGrain"), ThrowingGrain_TransportDispatcher.Instance);
                silo.UseLocalhostClustering(siloPort: SiloPort, gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                silo.Services.AddTcpTransport();
            })
            .Build();

        _clientHost = Host.CreateDefaultBuilder()
            .UseQuarkClient(client =>
            {
                client.UseLocalhostGateway(GatewayPort);
                client.Services.AddGrainProxy<IThrowingGrain, ThrowingGrainProxy>();
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

    [Fact]
    public async Task Guarantee1_BehaviorThrow_ReachesClient_WithServerErrorText_OverTcp()
    {
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();
        IThrowingGrain grain = client.GetGrain<IThrowingGrain>("g1");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.ThrowAsync("boom over tcp"));

        Assert.Contains("boom over tcp", ex.Message);
    }

    // =========================================================================
    // Grain interface, state, behavior
    // =========================================================================

    public interface IThrowingGrain : IGrainWithStringKey
    {
        Task ThrowAsync(string message);
    }

    private sealed class ThrowingGrainState;

    private sealed class ThrowingGrainBehavior : IGrainBehavior, IThrowingGrain
    {
        public ThrowingGrainBehavior(IActivationMemory<ThrowingGrainState> _) { }

        public Task ThrowAsync(string message) => throw new InvalidOperationException(message);
    }

    // =========================================================================
    // Proxy (client-side)
    // =========================================================================

    private sealed class ThrowingGrainProxy
        : IThrowingGrain, IGrainProxyActivator<ThrowingGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public ThrowingGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public GrainId GrainId => _grainId;

        public static ThrowingGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task ThrowAsync(string message)
            => _invoker.InvokeVoidAsync(_grainId, new ThrowingGrainProxy_ThrowInvokable(message)).AsTask();
    }

    // =========================================================================
    // Invokable
    // =========================================================================

    private readonly struct ThrowingGrainProxy_ThrowInvokable : IGrainVoidInvokable
    {
        private readonly string _message;
        public ThrowingGrainProxy_ThrowInvokable(string message) => _message = message;
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IThrowingGrain)behavior).ThrowAsync(_message));
        public void Serialize(ref CodecWriter writer) => writer.WriteString(_message);
        public static ThrowingGrainProxy_ThrowInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(reader.ReadString());
    }

    // =========================================================================
    // Transport dispatcher (silo-side)
    // =========================================================================

    private sealed class ThrowingGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly ThrowingGrain_TransportDispatcher Instance = new();

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
                    var inv = ThrowingGrainProxy_ThrowInvokable.Deserialize(ref reader, factory);
                    await invoker.InvokeVoidAsync<ThrowingGrainProxy_ThrowInvokable>(grainId, inv, ct);
                    return ReadOnlyMemory<byte>.Empty;
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for ThrowingGrain.");
        }
    }
}
