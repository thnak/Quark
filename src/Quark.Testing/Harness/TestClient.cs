using Microsoft.Extensions.DependencyInjection;

namespace Quark.Testing.Harness;

/// <summary>
/// Lightweight testing client facade exposed by <see cref="TestCluster"/>.
/// Follows Orleans TestCluster concept where tests interact with grains via a cluster-level client.
/// </summary>
public sealed class TestClient(IServiceProvider services) : IGrainFactory, IAsyncDisposable
{
    private bool _isInitialized;

    /// <summary>Gets whether the test client is connected.</summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>Underlying service provider used by the client.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Connects the test client.</summary>
    public Task ConnectAsync()
    {
        _isInitialized = true;
        return Task.CompletedTask;
    }

    /// <summary>Closes the test client.</summary>
    public Task CloseAsync()
    {
        _isInitialized = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _isInitialized = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>Resolves a service from the client container.</summary>
    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(string key) where TGrainInterface : IGrainWithStringKey =>
        GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key) where TGrainInterface : IGrainWithIntegerKey =>
        GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key) where TGrainInterface : IGrainWithGuidKey =>
        GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension) where TGrainInterface : IGrainWithIntegerCompoundKey =>
        GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key, keyExtension);

    /// <inheritdoc/>
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension) where TGrainInterface : IGrainWithGuidCompoundKey =>
        GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key, keyExtension);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, string key) =>
        GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, Guid key) =>
        GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);

    /// <inheritdoc/>
    public IGrain GetGrain(Type grainInterfaceType, long key) =>
        GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);
}
