using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Transport.Abstractions;
using Xunit;

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

    [Fact]
    public async Task Request_Message_Is_Dispatched_To_Grain_And_Returns_Response()
    {
        GrainInvocationRequest request = new(
            new GrainId(new GrainType("DispatchCounterGrain"), "counter-1"),
            DispatchCounterGrainMethodInvoker.IncrementMethodId,
            null);

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
                DispatchCounterGrainMethodInvoker.IncrementMethodId,
                null))
        };

        _ = await _fixture.Dispatcher.DispatchAsync(increment);

        MessageEnvelope reset = new()
        {
            CorrelationId = 3,
            MessageType = MessageType.OneWayRequest,
            Payload = _fixture.Serializer.SerializeRequest(new GrainInvocationRequest(
                grainId,
                DispatchCounterGrainMethodInvoker.ResetMethodId,
                null))
        };

        MessageEnvelope? oneWayResponse = await _fixture.Dispatcher.DispatchAsync(reset);
        Assert.Null(oneWayResponse);

        MessageEnvelope getValue = new()
        {
            CorrelationId = 4,
            MessageType = MessageType.Request,
            Payload = _fixture.Serializer.SerializeRequest(new GrainInvocationRequest(
                grainId,
                DispatchCounterGrainMethodInvoker.GetValueMethodId,
                null))
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

    public sealed class DispatchCounterGrainMethodInvoker : IGrainMethodInvoker
    {
        public const uint IncrementMethodId = 0;
        public const uint GetValueMethodId = 1;
        public const uint ResetMethodId = 2;

        public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
        {
            var typed = (DispatchCounterGrain)grain;
            return methodId switch
            {
                IncrementMethodId => await typed.IncrementAsync(),
                GetValueMethodId => await typed.GetValueAsync(),
                ResetMethodId => await typed.ResetAsync().ContinueWith(_ => (object?)null),
                _ => throw new NotSupportedException($"Unknown method id {methodId}")
            };
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
            services.AddTransient<DispatchCounterGrain>();
            services.AddSingleton<DispatchCounterGrainMethodInvoker>();

            _serviceProvider = services.BuildServiceProvider();

            GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("DispatchCounterGrain"), typeof(DispatchCounterGrain));

            GrainMethodInvokerRegistry invokerRegistry =
                _serviceProvider.GetRequiredService<GrainMethodInvokerRegistry>();
            invokerRegistry.Register(typeof(DispatchCounterGrain),
                _serviceProvider.GetRequiredService<DispatchCounterGrainMethodInvoker>());

            _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            IGrainActivator activator = _serviceProvider.GetRequiredService<IGrainActivator>();
            IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
            IOptions<SiloRuntimeOptions> siloOptions =
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();
            NullLogger<LocalGrainCallInvoker> logger = NullLogger<LocalGrainCallInvoker>.Instance;
            NullLogger<GrainActivation> logger2 = NullLogger<GrainActivation>.Instance;
            IGrainMethodInvokerRegistry methodInvokerReg =
                _serviceProvider.GetRequiredService<IGrainMethodInvokerRegistry>();
            IGrainFactory grainFactory = _serviceProvider.GetRequiredService<IGrainFactory>();

            LocalGrainCallInvoker callInvoker = new(
                _activationTable,
                activator,
                typeRegistry,
                directory,
                methodInvokerReg,
                grainFactory,
                _serviceProvider,
                siloOptions,
                logger,
                logger2);

            Serializer = new GrainMessageSerializer();
            Dispatcher = new MessageDispatcher(callInvoker, Serializer);
        }

        public IMessageDispatcher Dispatcher { get; }
        public GrainMessageSerializer Serializer { get; }

        public async ValueTask DisposeAsync()
        {
            await _activationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }
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
