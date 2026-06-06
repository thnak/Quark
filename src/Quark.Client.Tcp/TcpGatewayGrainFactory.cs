using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IGrainFactory" /> for the TCP gateway client.
///     Delegates to a <see cref="LocalGrainFactory" /> wired with a <see cref="TcpGatewayCallInvoker" />.
///     Observer references are not supported.
/// </summary>
public sealed class TcpGatewayGrainFactory : IGrainFactory
{
    private readonly LocalGrainFactory _inner;

    public TcpGatewayGrainFactory(
        GrainProxyFactoryRegistry proxyRegistry,
        GrainInterfaceTypeRegistry interfaceRegistry,
        TcpGatewayCallInvoker invoker)
    {
        _inner = new LocalGrainFactory(proxyRegistry, interfaceRegistry, invoker);
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
        => throw new NotSupportedException(
            "Observer references are local-only and cannot be created on a TCP gateway client.");

    public void DeleteObjectReference<TGrainObserver>(TGrainObserver reference)
        where TGrainObserver : class, IGrainObserver { }
}
