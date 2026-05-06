using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Testing.Harness;

/// <summary>
///     Lightweight testing client facade exposed by <see cref="TestCluster" />.
///     Follows Orleans TestCluster concept where tests interact with grains via a cluster-level client.
/// </summary>
public sealed class TestClient(IServiceProvider services) : IGrainFactory, IAsyncDisposable
{
    /// <summary>Gets whether the test client is connected.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Underlying service provider used by the client.</summary>
    public IServiceProvider Services { get; } = services;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsInitialized = false;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(string key) where TGrainInterface : IGrainWithStringKey
    {
        return GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key) where TGrainInterface : IGrainWithIntegerKey
    {
        return GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key) where TGrainInterface : IGrainWithGuidKey
    {
        return GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
    {
        return GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key, keyExtension);
    }

    /// <inheritdoc />
    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
    {
        return GetRequiredService<IGrainFactory>().GetGrain<TGrainInterface>(key, keyExtension);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, string key)
    {
        return GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, Guid key)
    {
        return GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);
    }

    /// <inheritdoc />
    public IGrain GetGrain(Type grainInterfaceType, long key)
    {
        return GetRequiredService<IGrainFactory>().GetGrain(grainInterfaceType, key);
    }

    /// <summary>Connects the test client.</summary>
    public Task ConnectAsync()
    {
        IsInitialized = true;
        return Task.CompletedTask;
    }

    /// <summary>Closes the test client.</summary>
    public Task CloseAsync()
    {
        IsInitialized = false;
        return Task.CompletedTask;
    }

    /// <summary>Resolves a service from the client container.</summary>
    public T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
}
