using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;
using Quark.Networking.Abstractions;
using StackExchange.Redis;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Extension methods for configuring Redis-based clustering for Quark cluster clients.
/// </summary>
public static class RedisClusteringClientExtensions
{
    /// <summary>
    /// Adds Redis clustering to the Cluster Client with shared connection support.
    /// This client-only implementation discovers silos but does not register itself.
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
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Register the IConnectionMultiplexer
        builder.Services.TryAddSingleton<IConnectionMultiplexer>(sp =>
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

        // Register Redis cluster membership (client-only, read-only)
        builder.Services.TryAddSingleton<IQuarkClusterMembership>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisClientClusterMembership(redis);
        });
        builder.Services.TryAddSingleton<IClusterMembership>(sp =>
        {
            var quarkClusterMembership = sp.GetRequiredService<IQuarkClusterMembership>();
            return quarkClusterMembership;
        });

        // Register health monitor if enabled
        if (enableHealthMonitoring)
        {
            builder.Services.TryAddSingleton(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var healthOptions = new RedisConnectionHealthOptions();
                configureHealthOptions?.Invoke(healthOptions);
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<RedisConnectionHealthMonitor>>();

                return new RedisConnectionHealthMonitor(redis, healthOptions, logger);
            });
        }

        return builder;
    }
}
