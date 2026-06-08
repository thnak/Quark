using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
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
        var reader1 = new CodecReader(response.ResultPayload);
        Assert.Equal(1L, reader1.ReadInt64());
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
        var reader2 = new CodecReader(valueResponse.ResultPayload);
        Assert.Equal(0L, reader2.ReadInt64());
    }

    public interface IDispatchCounterGrain : IGrainWithStringKey
    {
        Task<long> IncrementAsync();
        Task<long> GetValueAsync();
        Task ResetAsync();
    }

    public sealed class DispatchCounterBehavior : IGrainBehavior, IDispatchCounterGrain
    {
        private readonly IActivationMemory<DispatchCounterState> _memory;

        public DispatchCounterBehavior(IActivationMemory<DispatchCounterState> memory)
        {
            _memory = memory;
        }

        public Task<long> IncrementAsync()
        {
            _memory.Value.Value++;
            return Task.FromResult(_memory.Value.Value);
        }

        public Task<long> GetValueAsync()
        {
            return Task.FromResult(_memory.Value.Value);
        }

        public Task ResetAsync()
        {
            _memory.Value.Value = 0;
            return Task.CompletedTask;
        }
    }

    public sealed class DispatchCounterState
    {
        public long Value { get; set; }
    }

    // Hand-written invokables (mirror what GrainProxyGenerator would emit)
    private readonly struct DispatchCounterGrainProxy_IncrementAsyncInvokable : IGrainInvokable<long>
    {
        public uint MethodId => 0u;
        public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((IDispatchCounterGrain)behavior).IncrementAsync());
        public void Serialize(ref CodecWriter writer) { }
        public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
    }

    private readonly struct DispatchCounterGrainProxy_GetValueAsyncInvokable : IGrainInvokable<long>
    {
        public uint MethodId => 1u;
        public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((IDispatchCounterGrain)behavior).GetValueAsync());
        public void Serialize(ref CodecWriter writer) { }
        public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
    }

    private readonly struct DispatchCounterGrainProxy_ResetAsyncInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 2u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IDispatchCounterGrain)behavior).ResetAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    // Hand-written transport dispatcher (mirror what GrainProxyGenerator would emit)
    private sealed class DispatchCounterGrainProxy_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly DispatchCounterGrainProxy_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory, CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                {
                    long result = await invoker.InvokeAsync<DispatchCounterGrainProxy_IncrementAsyncInvokable, long>(grainId, new(), ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var w = new CodecWriter(buf);
                    w.WriteInt64(result);
                    return buf.WrittenMemory.ToArray();
                }
                case 1u:
                {
                    long result = await invoker.InvokeAsync<DispatchCounterGrainProxy_GetValueAsyncInvokable, long>(grainId, new(), ct);
                    var buf = new ArrayBufferWriter<byte>();
                    var w = new CodecWriter(buf);
                    w.WriteInt64(result);
                    return buf.WrittenMemory.ToArray();
                }
                case 2u:
                    await invoker.InvokeVoidAsync<DispatchCounterGrainProxy_ResetAsyncInvokable>(grainId, new(), ct);
                    return ReadOnlyMemory<byte>.Empty;
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

            // Behavior registration (replaces IGrainActivatorFactory)
            services.AddGrainBehavior<IDispatchCounterGrain, DispatchCounterBehavior>();

            // Scoped activation memory for the behavior
            services.AddScoped<IActivationMemory<DispatchCounterState>>(sp =>
                new ActivationMemoryAccessor<DispatchCounterState>(
                    sp.GetRequiredService<IActivationShellAccessor>()
                      .Shell.GetOrCreateHolder<DispatchCounterState>()));

            _serviceProvider = services.BuildServiceProvider();

            _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();

            var dispatcherRegistry = _serviceProvider.GetRequiredService<TransportGrainDispatcherRegistry>();
            dispatcherRegistry.Register(
                new GrainType("DispatchCounterGrain"),
                DispatchCounterGrainProxy_TransportDispatcher.Instance);

            Serializer = new GrainMessageSerializer();
            Dispatcher = _serviceProvider.GetRequiredService<IMessageDispatcher>();
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
