using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
/// Concrete <see cref="IGrainFactory"/> for in-process / cohosted scenarios.
/// Creates grain proxy objects that route calls through the silo's <see cref="IGrainCallInvoker"/>.
/// </summary>
public sealed class LocalGrainFactory : IGrainFactory
{
    private readonly GrainProxyFactoryRegistry _proxyRegistry;
    private readonly GrainInterfaceTypeRegistry _interfaceRegistry;
    private readonly IGrainCallInvoker _invoker;

    /// <summary>Initialises the factory.</summary>
    public LocalGrainFactory(
        GrainProxyFactoryRegistry proxyRegistry,
        GrainInterfaceTypeRegistry interfaceRegistry,
        IGrainCallInvoker invoker)
    {
        _proxyRegistry = proxyRegistry;
        _interfaceRegistry = interfaceRegistry;
        _invoker = invoker;
    }

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
    {
        var grainId = GrainIdForInterface<TGrainInterface>(key);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
    {
        var grainId = GrainIdForInterface<TGrainInterface>(key.ToString());
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
    {
        var grainId = GrainIdForInterface<TGrainInterface>(key.ToString("N"));
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
    {
        var rawKey = keyExtension is null ? key.ToString() : $"{key}+{keyExtension}";
        var grainId = GrainIdForInterface<TGrainInterface>(rawKey);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
    {
        var rawKey = keyExtension is null ? key.ToString("N") : $"{key:N}+{keyExtension}";
        var grainId = GrainIdForInterface<TGrainInterface>(rawKey);
        return _proxyRegistry.CreateProxy<TGrainInterface>(grainId, _invoker);
    }

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, string key)
    {
        var grainId = GrainIdForInterface(grainInterfaceType, key);
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, Guid key)
    {
        var grainId = GrainIdForInterface(grainInterfaceType, key.ToString("N"));
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, long key)
    {
        var grainId = GrainIdForInterface(grainInterfaceType, key.ToString());
        return _proxyRegistry.CreateProxy(grainInterfaceType, grainId, _invoker);
    }

    // -----------------------------------------------------------------------

    private GrainId GrainIdForInterface<TInterface>(string key)
        => GrainId.Create(_interfaceRegistry.GetGrainType(typeof(TInterface)), key);

    private GrainId GrainIdForInterface(Type interfaceType, string key)
        => GrainId.Create(_interfaceRegistry.GetGrainType(interfaceType), key);
}
