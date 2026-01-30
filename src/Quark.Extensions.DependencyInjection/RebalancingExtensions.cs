using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring actor rebalancing services.
/// </summary>
public static class RebalancingExtensions
{
    /// <summary>
    /// Adds dynamic actor rebalancing to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddActorRebalancing(
        this IServiceCollection services,
        Action<RebalancingOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Register options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<RebalancingOptions>(_ => { });
        }

        // Register rebalancer
        services.TryAddSingleton<IActorRebalancer, LoadBasedRebalancer>();

        return services;
    }
}
