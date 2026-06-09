using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IGrainFactory" /> for the TCP gateway client.
///     Delegates grain lookups to an inner <see cref="LocalGrainFactory" />.
///     Observer references are supported: <see cref="CreateObjectReference{T}" /> registers the
///     local implementation and sends an <see cref="MessageType.ObserverRegister" /> frame to the
///     silo so back-channel invocations can be routed over the open TCP connection.
/// </summary>
public sealed class TcpGatewayGrainFactory : IGrainFactory
{
    private readonly LocalGrainFactory _inner;
    private readonly ObserverRegistry? _observerRegistry;
    private readonly ObserverProxyFactoryRegistry? _observerProxyRegistry;
    private readonly TcpGatewayConnection? _connection;

    public TcpGatewayGrainFactory(
        GrainProxyFactoryRegistry proxyRegistry,
        GrainInterfaceTypeRegistry interfaceRegistry,
        TcpGatewayCallInvoker invoker,
        ObserverRegistry? observerRegistry = null,
        ObserverProxyFactoryRegistry? observerProxyRegistry = null,
        TcpGatewayConnection? connection = null)
    {
        _inner = new LocalGrainFactory(proxyRegistry, interfaceRegistry, invoker,
            observerProxyRegistry, observerRegistry);
        _observerRegistry = observerRegistry;
        _observerProxyRegistry = observerProxyRegistry;
        _connection = connection;
    }

    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
        => _inner.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
        => _inner.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
        => _inner.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
        => _inner.GetGrain<TGrainInterface>(key, keyExtension);

    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
        => _inner.GetGrain<TGrainInterface>(key, keyExtension);

    public IGrain GetGrain(Type grainInterfaceType, string key)
        => _inner.GetGrain(grainInterfaceType, key);

    public IGrain GetGrain(Type grainInterfaceType, Guid key)
        => _inner.GetGrain(grainInterfaceType, key);

    public IGrain GetGrain(Type grainInterfaceType, long key)
        => _inner.GetGrain(grainInterfaceType, key);

    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId)
        where TGrainInterface : IGrain
        => _inner.GetGrain<TGrainInterface>(grainId);

    public TGrainObserver CreateObjectReference<TGrainObserver>(TGrainObserver implementation)
        where TGrainObserver : class, IGrainObserver
    {
        if (_observerRegistry is null || _observerProxyRegistry is null)
            throw new InvalidOperationException(
                "Observer support is not configured. " +
                "Register ObserverRegistry and ObserverProxyFactoryRegistry during client startup.");

        var grainType = new GrainType($"observer:{typeof(TGrainObserver).Name}");
        GrainId grainId = GrainId.Create(grainType, Guid.NewGuid().ToString("N"));
        _observerRegistry.Register(grainId, implementation);

        if (_connection is not null)
        {
            var headers = new MessageHeaders();
            headers.Set("grain-type", grainId.Type.Value);
            headers.Set("grain-key", grainId.Key);
            Task send = _connection.SendOneWayAsync(new MessageEnvelope
            {
                MessageType = MessageType.ObserverRegister,
                CorrelationId = -1,
                Headers = headers,
                Payload = ReadOnlyMemory<byte>.Empty
            });
            // If the send fails, unregister locally so the broken proxy is cleaned up.
            _ = send.ContinueWith(
                t => _observerRegistry!.Unregister(grainId),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        return _inner.GetObserverRef<TGrainObserver>(grainId);
    }

    public void DeleteObjectReference<TGrainObserver>(TGrainObserver reference)
        where TGrainObserver : class, IGrainObserver
    {
        if (reference is not IGrainObserverProxy proxy) return;
        GrainId grainId = proxy.GrainId;
        _observerRegistry?.Unregister(grainId);
        if (_connection is null) return;
        var headers = new MessageHeaders();
        headers.Set("grain-type", grainId.Type.Value);
        headers.Set("grain-key", grainId.Key);
        _ = _connection.SendOneWayAsync(new MessageEnvelope
        {
            MessageType = MessageType.ObserverUnregister,
            CorrelationId = -1,
            Headers = headers,
            Payload = ReadOnlyMemory<byte>.Empty
        });
    }

    public TGrainObserver GetObserverRef<TGrainObserver>(GrainId grainId)
        where TGrainObserver : class, IGrainObserver
        => _inner.GetObserverRef<TGrainObserver>(grainId);
}
