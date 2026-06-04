using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Persistence.Redis;

/// <summary>
///     Service registration helpers for the Redis persistence provider.
/// </summary>
public static class RedisGrainStorageServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Redis grain storage provider using optional options configuration.
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
    ///     Registers the Redis grain storage provider using a connection string.
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

    /// <summary>
    ///     Registers a named Redis grain storage provider resolvable via
    ///     <c>GetRequiredKeyedService&lt;IGrainStorage&gt;(name)</c>.
    ///     Use with <c>[PersistentState("slot", "<paramref name="name" />")]</c>.
    /// </summary>
    public static IServiceCollection AddKeyedRedisGrainStorage(
        this IServiceCollection services,
        string name,
        Action<RedisStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // Each named provider gets its own connection and options so that separate
        // providers can point to different Redis endpoints / use different key prefixes.
        var options = new RedisStorageOptions();
        configure?.Invoke(options);
        IOptions<RedisStorageOptions> frozenOptions = Options.Create(options);

        services.AddKeyedSingleton<IRedisStorageConnection>(name,
            (_, _) => new RedisStorageConnection(frozenOptions));
        services.AddKeyedSingleton<IGrainStorage>(name,
            (sp, _) => new RedisGrainStorage(
                sp.GetRequiredKeyedService<IRedisStorageConnection>(name),
                sp.GetRequiredService<ISerializer>(),
                frozenOptions));
        return services;
    }
}
