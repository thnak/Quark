using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Persistence;
using Quark.Abstractions.Reminders;
using Quark.Clustering.Redis;
using Quark.Hosting;
using Quark.Networking.Abstractions;
using Quark.Storage.Redis;
using StackExchange.Redis;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Redis-based clustering and storage with connection optimization.
/// </summary>
public static class RedisClusteringExtensions
{
    /// <summary>
    /// Adds Redis clustering to the Cluster Client with shared connection support.
    /// This method allows you to provide an existing IConnectionMultiplexer to avoid duplicate connections.
    /// </summary>
    /// <param name="builder">The client builder.</param>
    /// <param name="connectionMultiplexer">Optional shared Redis connection. If null, a new connection will be created using the connectionString.</param>
    /// <param name="connectionString">Connection string for Redis. Required if connectionMultiplexer is null.</param>
    /// <param name="options">Optional configuration options for Redis connection.</param>
    /// <param name="enableHealthMonitoring">Whether to enable connection health monitoring. Defaults to true.</param>
    /// <param name="configureHealthOptions">Optional action to configure health monitoring options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IClusterClientBuilder WithRedisClustering(
        this IClusterClientBuilder builder,
        IConnectionMultiplexer? connectionMultiplexer = null,
        string? connectionString = null,
        ConfigurationOptions? options = null,
        bool enableHealthMonitoring = true,
        Action<RedisConnectionHealthOptions>? configureHealthOptions = null)
    {
        builder.Services.WithRedisClustering(connectionMultiplexer, connectionString, options, enableHealthMonitoring,
            configureHealthOptions);
        return builder;
    }

    /// <summary>
    /// Adds Redis clustering to the Quark Silo with shared connection support.
    /// This method allows you to provide an existing IConnectionMultiplexer to avoid duplicate connections.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="connectionMultiplexer">Optional shared Redis connection. If null, a new connection will be created using the connectionString.</param>
    /// <param name="connectionString">Connection string for Redis. Required if connectionMultiplexer is null.</param>
    /// <param name="options">Optional configuration options for Redis connection.</param>
    /// <param name="enableHealthMonitoring">Whether to enable connection health monitoring. Defaults to true.</param>
    /// <param name="configureHealthOptions">Optional action to configure health monitoring options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithRedisClustering(
        this IQuarkSiloBuilder builder,
        IConnectionMultiplexer? connectionMultiplexer = null,
        string? connectionString = null,
        ConfigurationOptions? options = null,
        bool enableHealthMonitoring = true,
        Action<RedisConnectionHealthOptions>? configureHealthOptions = null)
    {
        builder.Services.WithRedisClustering(connectionMultiplexer, connectionString, options, enableHealthMonitoring,
            configureHealthOptions);
        return builder;
    }

    public static void WithRedisClustering(this IServiceCollection services,
        IConnectionMultiplexer? connectionMultiplexer = null,
        string? connectionString = null,
        ConfigurationOptions? options = null,
        bool enableHealthMonitoring = true,
        Action<RedisConnectionHealthOptions>? configureHealthOptions = null)
    {
        // Register the IConnectionMultiplexer
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            // Use provided multiplexer or create a new one
            if (connectionMultiplexer != null)
            {
                return connectionMultiplexer;
            }

            if (!string.IsNullOrEmpty(connectionString))
            {
                return ConnectionMultiplexer.Connect(connectionString);
            }

            if (options != null)
            {
                return ConnectionMultiplexer.Connect(options);
            }

            throw new InvalidOperationException(
                "Either connectionMultiplexer, connectionString, or options must be provided.");
        });

        // Register Redis cluster membership
        services.TryAddSingleton<IClusterMembership>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var siloOptions = sp.GetRequiredService<QuarkSiloOptions>();
            var siloId = siloOptions.SiloId ?? Guid.NewGuid().ToString("N");

            return new RedisClusterMembership(redis, siloId);
        });
        services.TryAddSingleton<IQuarkClusterMembership>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var siloOptions = sp.GetRequiredService<QuarkSiloOptions>();
            var siloId = siloOptions.SiloId ?? Guid.NewGuid().ToString("N");

            return new RedisClusterMembership(redis, siloId);
        });

        // Register health monitor if enabled
        if (enableHealthMonitoring)
        {
            services.TryAddSingleton(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var healthOptions = new RedisConnectionHealthOptions();
                configureHealthOptions?.Invoke(healthOptions);
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<RedisConnectionHealthMonitor>>();

                return new RedisConnectionHealthMonitor(redis, healthOptions, logger);
            });
        }
    }

    /// <summary>
    /// Adds Redis state storage to the Quark Silo with shared connection support.
    /// Uses the existing IConnectionMultiplexer registered in the container.
    /// </summary>
    /// <typeparam name="TState">The type of state to store.</typeparam>
    /// <param name="builder">The silo builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithRedisStateStorage<TState>(this IQuarkSiloBuilder builder)
        where TState : class
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.TryAddSingleton<IStateStorage<TState>>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var db = redis.GetDatabase();
            return new RedisStateStorage<TState>(db);
        });

        return builder;
    }

    /// <summary>
    /// Adds Redis reminder storage to the Quark Silo with shared connection support.
    /// Uses the existing IConnectionMultiplexer registered in the container.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithRedisReminderStorage(this IQuarkSiloBuilder builder)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.TryAddSingleton<IReminderTable>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var db = redis.GetDatabase();

            // Try to get the Redis membership to access hash ring
            IConsistentHashRing? hashRing = null;
            if (sp.GetService<IClusterMembership>() is RedisClusterMembership redisMembership)
            {
                hashRing = redisMembership.HashRing;
            }

            return new RedisReminderTable(db, hashRing);
        });

        return builder;
    }
}