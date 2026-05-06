using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Persistence.Abstractions;

namespace Quark.Persistence.InMemory;

/// <summary>
/// Service registration helpers for the in-memory persistence provider.
/// </summary>
public static class InMemoryGrainStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory grain storage provider as the default persistence backend.
    /// Orleans-compatible alias: <c>AddMemoryGrainStorage()</c>.
    /// </summary>
    public static IServiceCollection AddInMemoryGrainStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IGrainStorage, InMemoryGrainStorage>();
        services.TryAddSingleton(typeof(IStorage<>), typeof(InMemoryStorage<>));
        return services;
    }

    /// <summary>
    /// Orleans-compatible alias for registering an in-memory grain storage provider.
    /// </summary>
    public static IServiceCollection AddMemoryGrainStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null) =>
        services.AddInMemoryGrainStorage(configure);
}