using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions.Abstractions; // ICopierProvider

namespace Quark.Persistence.InMemory;

/// <summary>
///     Service registration helpers for the in-memory persistence provider.
/// </summary>
public static class InMemoryGrainStorageServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-memory grain storage provider as the default persistence backend.
    ///     Orleans-compatible alias: <c>AddMemoryGrainStorage()</c>.
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
    ///     Registers a named in-memory grain storage provider resolvable via
    ///     <c>GetRequiredKeyedService&lt;IGrainStorage&gt;(name)</c>.
    ///     Use with <c>[PersistentState("slot", "<paramref name="name" />")]</c>.
    /// </summary>
    public static IServiceCollection AddInMemoryGrainStorage(
        this IServiceCollection services,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        services.AddKeyedSingleton<IGrainStorage>(name,
            (sp, _) => new InMemoryGrainStorage(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }

    /// <summary>
    ///     Orleans-compatible alias for registering an in-memory grain storage provider.
    /// </summary>
    public static IServiceCollection AddMemoryGrainStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null)
    {
        return services.AddInMemoryGrainStorage(configure);
    }
}
