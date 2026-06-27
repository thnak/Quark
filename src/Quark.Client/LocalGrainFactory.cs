using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Client;

/// <summary>
///     Concrete <see cref="IGrainFactory" /> for in-process / cohosted scenarios.
///     Creates grain proxy objects that route calls through the silo's <see cref="IGrainCallInvoker" />.
/// </summary>
public sealed class LocalGrainFactory : IGrainFactory
{
    private readonly GrainInterfaceTypeRegistry _interfaceRegistry;
    private readonly IGrainCallInvoker _invoker;
    private readonly ObserverProxyFactoryRegistry? _observerProxyRegistry;
    private readonly ObserverRegistry? _observerRegistry;
    private readonly GrainProxyFactoryRegistry _proxyRegistry;

    /// <summary>Initialises the factory.</summary>
    public LocalGrainFactory(
        GrainProxyFactoryRegistry proxyRegistry,
        GrainInterfaceTypeRegistry interfaceRegistry,
        IGrainCallInvoker invoker,
        ObserverProxyFactoryRegistry? observerProxyRegistry = null,
        ObserverRegistry? observerRegistry = null)
    {
        _proxyRegistry = proxyRegistry;
        _interfaceRegistry = interfaceRegistry;
        _invoker = invoker;
        _observerProxyRegistry = observerProxyRegistry;
        _observerRegistry = observerRegistry;
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
    {
        GrainId grainId = GrainIdForInterface<TGrainInterface>(key);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
    {
        GrainId grainId = GrainIdForInterface<TGrainInterface>(key.ToString());
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
    {
        GrainId grainId = GrainIdForInterface<TGrainInterface>(key.ToString("N"));
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
    {
        string rawKey = keyExtension is null ? key.ToString() : $"{key}+{keyExtension}";
        GrainId grainId = GrainIdForInterface<TGrainInterface>(rawKey);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
    {
        string rawKey = keyExtension is null ? key.ToString("N") : $"{key:N}+{keyExtension}";
        GrainId grainId = GrainIdForInterface<TGrainInterface>(rawKey);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, string key)
    {
        GrainId grainId = GrainIdForInterface(grainInterfaceType, key);
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, Guid key)
    {
        GrainId grainId = GrainIdForInterface(grainInterfaceType, key.ToString("N"));
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, long key)
    {
        GrainId grainId = GrainIdForInterface(grainInterfaceType, key.ToString());
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId)
        where TGrainInterface : IGrain
        => _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);

    /// <inheritdoc />
    public TGrainObserver CreateObjectReference<TGrainObserver>(TGrainObserver implementation)
        where TGrainObserver : class, IGrainObserver
    {
        if (_observerProxyRegistry is null || _observerRegistry is null)
        {
            throw new InvalidOperationException(
                "Observer support is not configured. " +
                "Call AddObserverProxy<TInterface, TProxy>() during startup.");
        }

        var grainType = new GrainType($"observer:{typeof(TGrainObserver).Name}");
        var grainId = GrainId.Create(grainType, Guid.NewGuid().ToString("N"));
        _observerRegistry.Register(grainId, implementation);
        return _observerProxyRegistry.CreateProxy<TGrainObserver>(grainId, _invoker);
    }

    /// <inheritdoc />
    public void DeleteObjectReference<TGrainObserver>(TGrainObserver reference)
        where TGrainObserver : class, IGrainObserver
    {
        _observerRegistry?.UnregisterByTarget(reference);
    }

    /// <inheritdoc />
    public TGrainObserver GetObserverRef<TGrainObserver>(GrainId grainId)
        where TGrainObserver : class, IGrainObserver
    {
        if (_observerProxyRegistry is null)
        {
            throw new InvalidOperationException(
                "Observer support is not configured. " +
                "Call AddObserverProxy<TInterface, TProxy>() during startup.");
        }

        return _observerProxyRegistry.CreateProxy<TGrainObserver>(grainId, _invoker);
    }

    // -----------------------------------------------------------------------

    private GrainId GrainIdForInterface<TInterface>(string key)
    {
        return GrainId.Create(_interfaceRegistry.GetGrainType(typeof(TInterface)), key);
    }

    private GrainId GrainIdForInterface(Type interfaceType, string key)
    {
        return GrainId.Create(_interfaceRegistry.GetGrainType(interfaceType), key);
    }
}
