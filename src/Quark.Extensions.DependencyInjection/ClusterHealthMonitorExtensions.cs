using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering cluster health monitoring.
/// </summary>
public static class ClusterHealthMonitorExtensions
{
    /// <summary>
    ///     Adds cluster health monitoring to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for eviction policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddClusterHealthMonitoring(
        this IServiceCollection services,
        Action<EvictionPolicyOptions>? configureOptions = null)
    {
        // Register health score calculator
        services.AddSingleton<IHealthScoreCalculator, DefaultHealthScoreCalculator>();

        // Register eviction policy options
        var options = new EvictionPolicyOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        // Register cluster health monitor
        services.AddSingleton<IClusterHealthMonitor, ClusterHealthMonitor>();

        return services;
    }
}
