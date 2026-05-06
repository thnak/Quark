using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quark.Persistence.Redis;

/// <summary>
/// Service registration helpers for the Redis persistence provider.
/// </summary>
public static class RedisGrainStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redis grain storage provider using optional options configuration.
    /// </summary>
    public static IServiceCollection AddRedisGrainStorage(
        this IServiceCollection services,
        Action<RedisStorageOptions>? configure = null)
    {
        services.AddOptions<RedisStorageOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IRedisStorageConnection, RedisStorageConnection>();
        services.TryAddSingleton<IGrainStorage, RedisGrainStorage>();
        services.TryAddSingleton(typeof(IStorage<>), typeof(RedisStorage<>));
        return services;
    }

    /// <summary>
    /// Registers the Redis grain storage provider using a connection string.
    /// </summary>
    public static IServiceCollection AddRedisGrainStorage(
        this IServiceCollection services,
        string connectionString,
        Action<RedisStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddRedisGrainStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }
}