using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Xunit;

#pragma warning disable CS1998 // async method without await

namespace Quark.Tests.Integration;

public sealed class MessageDispatchIntegrationTests : IAsyncLifetime
{
    private MessageDispatchFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new MessageDispatchFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return _fixture.DisposeAsync().AsTask();
    }

    private const uint IncrementMethodId = 0u;
    private const uint GetValueMethodId = 1u;
    private const uint ResetMethodId = 2u;

    [Fact]
    public async Task Request_Message_Is_Dispatched_To_Grain_And_Returns_Response()
    {
        GrainInvocationRequest request = new(
            new GrainId(new GrainType("DispatchCounterGrain"), "counter-1"),
            IncrementMethodId,
            ReadOnlyMemory<byte>.Empty);

        MessageEnvelope envelope = new()
        {
            CorrelationId = 1,
            MessageType = MessageType.Request,
            Payload = _fixture.Serializer.SerializeRequest(request)
        };

        MessageEnvelope? responseEnvelope = await _fixture.Dispatcher.DispatchAsync(envelope);

        Assert.NotNull(responseEnvelope);
        Assert.Equal(MessageType.Response, responseEnvelope.MessageType);

        GrainInvocationResponse response = _fixture.Serializer.DeserializeResponse(responseEnvelope.Payload.ToArray());
        Assert.True(response.Success);
        Assert.Equal(1L, response.Result);
    }

    [Fact]
    public async Task OneWay_Message_Is_Dispatched_Without_Response()
    {
        GrainId grainId = new(new GrainType("DispatchCounterGrain"), "counter-2");

        MessageEnvelope increment = new()
        {
            CorrelationId = 2,
            MessageType = MessageType.Request,
            Payload = _fixture.Serializer.SerializeRequest(new GrainInvocationRequest(
                grainId,
                IncrementMethodId,
                ReadOnlyMemory<byte>.Empty))
        };

        _ = await _fixture.Dispatcher.DispatchAsync(increment);

        MessageEnvelope reset = new()
        {
            CorrelationId = 3,
            MessageType = MessageType.OneWayRequest,
            Payload = _fixture.Serializer.SerializeRequest(new GrainInvocationRequest(
                grainId,
                ResetMethodId,
                ReadOnlyMemory<byte>.Empty))
        };

        MessageEnvelope? oneWayResponse = await _fixture.Dispatcher.DispatchAsync(reset);
        Assert.Null(oneWayResponse);

        MessageEnvelope getValue = new()
        {
            CorrelationId = 4,
            MessageType = MessageType.Request,
            Payload = _fixture.Serializer.SerializeRequest(new GrainInvocationRequest(
                grainId,
                GetValueMethodId,
                ReadOnlyMemory<byte>.Empty))
        };

        MessageEnvelope? valueResponseEnvelope = await _fixture.Dispatcher.DispatchAsync(getValue);
        GrainInvocationResponse valueResponse =
            _fixture.Serializer.DeserializeResponse(valueResponseEnvelope!.Payload.ToArray());

        Assert.True(valueResponse.Success);
        Assert.Equal(0L, valueResponse.Result);
    }

    public interface IDispatchCounterGrain : IGrainWithStringKey
    {
        Task<long> IncrementAsync();
        Task<long> GetValueAsync();
        Task ResetAsync();
    }

    public sealed class DispatchCounterGrain : Grain, IDispatchCounterGrain
    {
        private long _value;

        public Task<long> IncrementAsync()
        {
            return Task.FromResult(++_value);
        }

        public Task<long> GetValueAsync()
        {
            return Task.FromResult(_value);
        }

        public Task ResetAsync()
        {
            _value = 0;
            return Task.CompletedTask;
        }
    }

    // Hand-written invokables (mirror what GrainProxyGenerator would emit)
    private readonly struct DispatchCounterGrainProxy_IncrementAsyncInvokable : IGrainInvokable<long>
    {
        public uint MethodId => 0u;
        public ValueTask<long> Invoke(Grain grain) => new(((IDispatchCounterGrain)grain).IncrementAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct DispatchCounterGrainProxy_GetValueAsyncInvokable : IGrainInvokable<long>
    {
        public uint MethodId => 1u;
        public ValueTask<long> Invoke(Grain grain) => new(((IDispatchCounterGrain)grain).GetValueAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct DispatchCounterGrainProxy_ResetAsyncInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 2u;
        public ValueTask Invoke(Grain grain) => new(((IDispatchCounterGrain)grain).ResetAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    // Hand-written transport dispatcher (mirror what GrainProxyGenerator would emit)
    private sealed class DispatchCounterGrainProxy_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly DispatchCounterGrainProxy_TransportDispatcher Instance = new();

        public async Task<object?> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                    return await invoker.InvokeAsync<DispatchCounterGrainProxy_IncrementAsyncInvokable, long>(
                        grainId, new(), ct);
                case 1u:
                    return await invoker.InvokeAsync<DispatchCounterGrainProxy_GetValueAsyncInvokable, long>(
                        grainId, new(), ct);
                case 2u:
                    await invoker.InvokeVoidAsync<DispatchCounterGrainProxy_ResetAsyncInvokable>(
                        grainId, new(), ct);
                    return null;
            }
            throw new NotSupportedException($"Unknown method id {methodId}");
        }
    }

    private sealed class MessageDispatchFixture : IAsyncDisposable
    {
        private readonly GrainActivationTable _activationTable;
        private readonly ServiceProvider _serviceProvider;

        public MessageDispatchFixture()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddQuarkSerialization();
            services.Configure<SiloRuntimeOptions>(o =>
            {
                o.ClusterId = "test";
                o.ServiceId = "integration";
                o.SiloName = "silo0";
            });

            services.AddSingleton<IGrainFactory, NullGrainFactory>();
            services.AddQuarkRuntime();
            services.AddSingleton<IGrainActivatorFactory>(new DispatchCounterGrainActivatorFactory());

            _serviceProvider = services.BuildServiceProvider();

            GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("DispatchCounterGrain"), typeof(DispatchCounterGrain));

            _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            IGrainActivator activator = _serviceProvider.GetRequiredService<IGrainActivator>();
            IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
            IOptions<SiloRuntimeOptions> siloOptions =
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

            LocalGrainCallInvoker callInvoker = new(
                _activationTable,
                activator,
                typeRegistry,
                directory,
                _serviceProvider,
                siloOptions,
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance);

            var dispatcherRegistry = new TransportGrainDispatcherRegistry();
            dispatcherRegistry.Register(
                new GrainType("DispatchCounterGrain"),
                DispatchCounterGrainProxy_TransportDispatcher.Instance);

            Serializer = new GrainMessageSerializer();
            Dispatcher = new MessageDispatcher(dispatcherRegistry, callInvoker, Serializer);
        }

        public IMessageDispatcher Dispatcher { get; }
        public GrainMessageSerializer Serializer { get; }

        public async ValueTask DisposeAsync()
        {
            await _activationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }
    }

    private sealed class DispatchCounterGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(DispatchCounterGrain);
        public Grain Create(GrainId grainId, IServiceProvider services) => new DispatchCounterGrain();
    }

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, string key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, Guid key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, long key)
        {
            throw new NotImplementedException();
        }
    }
}
