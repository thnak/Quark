using Quark.Core.Abstractions;

namespace Quark.Client;

/// <summary>
/// In-process (cohosted) implementation of <see cref="IClusterClient"/>.
/// Routes calls directly to the local <see cref="IGrainCallInvoker"/> without a network hop.
/// Matches the Orleans pattern where silo + client run in the same process.
/// </summary>
public sealed class LocalClusterClient : IClusterClient
{
    private readonly LocalGrainFactory _factory;
    private bool _initialized;

    /// <summary>Initialises the client.</summary>
    public LocalClusterClient(LocalGrainFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    /// <inheritdoc/>
    public Task Connect(Func<Exception, Task>? retryFilter = null)
    {
        // For the local (in-process) client, connecting is a no-op — the silo is always accessible.
        _initialized = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Close()
    {
        _initialized = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _initialized = false;
        return ValueTask.CompletedTask;
    }

    // ---- IGrainFactory forwarding ----------------------------------------

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
        => _factory.GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
        => _factory.GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
        => _factory.GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
        => _factory.GetGrain<TGrainInterface>(key, keyExtension);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
        => _factory.GetGrain<TGrainInterface>(key, keyExtension);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, string key)
        => _factory.GetGrain(grainInterfaceType, key);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, Guid key)
        => _factory.GetGrain(grainInterfaceType, key);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, long key)
        => _factory.GetGrain(grainInterfaceType, key);
}
