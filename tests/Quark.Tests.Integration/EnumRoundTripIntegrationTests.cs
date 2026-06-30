using System.Buffers;
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

/// <summary>
///     Verifies that enum types round-trip correctly across the TCP gateway.
///     Uses ports distinct from GatewayIntegrationTests (11200/30100) and TcpObserverBackChannelTests (11202/30102).
/// </summary>
[Collection("GatewayTests")]
[Trait("category", "gateway")]
public sealed class EnumRoundTripIntegrationTests : IAsyncLifetime
{
    private const int SiloPort    = 11203;
    private const int GatewayPort = 30103;

    private readonly string _clusterId = $"enum-test-{Guid.NewGuid():N}";

    private IHost _siloHost = null!;
    private IHost _clientHost = null!;

    public async Task InitializeAsync()
    {
        _siloHost = Host.CreateDefaultBuilder()
            .UseQuark(silo =>
            {
                silo.Services.AddQuarkRuntime();
                silo.Services.AddGrainBehavior<IEnumGrain, EnumGrainBehavior>();
                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("EnumGrain"), EnumGrainProxy_TransportDispatcher.Instance);
                silo.UseLocalhostClustering(siloPort: SiloPort, gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                silo.Services.AddTcpTransport();
                silo.Services.AddLocalClusterClient();
                silo.Services.AddGrainProxy<IEnumGrain, EnumGrainProxy>();
            })
            .Build();

        _clientHost = Host.CreateDefaultBuilder()
            .UseQuarkClient(client =>
            {
                client.UseLocalhostGateway(GatewayPort);
                client.Services.AddGrainProxy<IEnumGrain, EnumGrainProxy>();
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
    public async Task Enum_Parameter_And_Return_Value_RoundTrip_Over_Tcp()
    {
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();
        IEnumGrain grain = client.GetGrain<IEnumGrain>("e1");

        TaskPriority result = await grain.EchoAsync(TaskPriority.High);

        Assert.Equal(TaskPriority.High, result);
    }

    [Theory]
    [InlineData(TaskPriority.Low)]
    [InlineData(TaskPriority.Normal)]
    [InlineData(TaskPriority.High)]
    public async Task All_Enum_Values_RoundTrip_Correctly(TaskPriority priority)
    {
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();
        IEnumGrain grain = client.GetGrain<IEnumGrain>("e-all");

        TaskPriority result = await grain.EchoAsync(priority);

        Assert.Equal(priority, result);
    }

    // =========================================================================
    // Enum type
    // =========================================================================

    public enum TaskPriority { Low = 0, Normal = 1, High = 2 }

    // =========================================================================
    // Grain interface
    // =========================================================================

    public interface IEnumGrain : IGrainWithStringKey
    {
        Task<TaskPriority> EchoAsync(TaskPriority priority);
    }

    // =========================================================================
    // Grain behavior
    // =========================================================================

    private sealed class EnumGrainBehavior : IGrainBehavior, IEnumGrain
    {
        public Task<TaskPriority> EchoAsync(TaskPriority priority) => Task.FromResult(priority);
    }

    // =========================================================================
    // Invokable struct — hand-written (avoids codegen circular ref in tests)
    // =========================================================================

    private readonly struct EnumGrainProxy_EchoAsyncInvokable : IGrainInvokable<TaskPriority>
    {
        private readonly TaskPriority _priority;

        public EnumGrainProxy_EchoAsyncInvokable(TaskPriority priority) => _priority = priority;

        public uint MethodId => 0u;

        public ValueTask<TaskPriority> Invoke(IGrainBehavior behavior)
            => new(((IEnumGrain)behavior).EchoAsync(_priority));

        public EnumGrainProxy_EchoAsyncInvokable Clone() => this;

        public void Serialize(ref CodecWriter writer)
            => writer.WriteInt32((int)_priority);

        public static EnumGrainProxy_EchoAsyncInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new((TaskPriority)reader.ReadInt32());

        public TaskPriority DeserializeResult(ref CodecReader reader)
            => (TaskPriority)reader.ReadInt32();
    }

    // =========================================================================
    // Proxy class
    // =========================================================================

    private sealed class EnumGrainProxy
        : IEnumGrain, IGrainProxyActivator<EnumGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public EnumGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public GrainId GrainId => _grainId;

        public static EnumGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task<TaskPriority> EchoAsync(TaskPriority priority)
            => _invoker.InvokeAsync<EnumGrainProxy_EchoAsyncInvokable, TaskPriority>(
                _grainId, new EnumGrainProxy_EchoAsyncInvokable(priority));
    }

    // =========================================================================
    // Transport dispatcher (silo-side)
    // =========================================================================

    private sealed class EnumGrainProxy_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly EnumGrainProxy_TransportDispatcher Instance = new();

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
                    var inv = EnumGrainProxy_EchoAsyncInvokable.Deserialize(ref reader, factory);
                    TaskPriority result = await invoker.InvokeAsync<EnumGrainProxy_EchoAsyncInvokable, TaskPriority>(
                        grainId, inv, ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var writer = new CodecWriter(buf);
                    writer.WriteInt32((int)result);
                    return buf.WrittenMemory.ToArray();
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for EnumGrain.");
        }
    }
}
