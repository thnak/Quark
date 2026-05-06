using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Client;

/// <summary>
///     In-process (cohosted) implementation of <see cref="IClusterClient" />.
///     Routes calls directly to the local <see cref="IGrainCallInvoker" /> without a network hop.
///     Matches the Orleans pattern where silo + client run in the same process.
/// </summary>
public sealed class LocalClusterClient : IClusterClient
{
    private readonly LocalGrainFactory _factory;

    /// <summary>Initialises the client.</summary>
    public LocalClusterClient(LocalGrainFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public Task Connect(Func<Exception, Task>? retryFilter = null)
    {
        // For the local (in-process) client, connecting is a no-op — the silo is always accessible.
        IsInitialized = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Close()
    {
        IsInitialized = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsInitialized = false;
        return ValueTask.CompletedTask;
    }

    // ---- IGrainFactory forwarding ----------------------------------------

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
    {
        return _factory.GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
    {
        return _factory.GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
    {
        return _factory.GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
    {
        return _factory.GetGrain<TGrainInterface>(key, keyExtension);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
    {
        return _factory.GetGrain<TGrainInterface>(key, keyExtension);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, string key)
    {
        return _factory.GetGrain(grainInterfaceType, key);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, Guid key)
    {
        return _factory.GetGrain(grainInterfaceType, key);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, long key)
    {
        return _factory.GetGrain(grainInterfaceType, key);
    }
}
