using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Abstractions.Clustering;
using Quark.Client;
using Quark.Networking.Abstractions;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring smart routing services for Quark silos.
/// </summary>
public static class SmartRoutingExtensions
{
    /// <summary>
    /// Adds smart routing with a specific local silo ID for co-hosted scenarios.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="localSiloId">The local silo ID for bypass optimization.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSmartRouting(
        this IServiceCollection services,
        string localSiloId,
        Action<SmartRoutingOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrEmpty(localSiloId))
        {
            throw new ArgumentException("Local silo ID cannot be null or empty.", nameof(localSiloId));
        }

        // Register options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<SmartRoutingOptions>(_ => { });
        }

        // Register smart router with local silo ID
        services.TryAddSingleton<ISmartRouter>(sp =>
        {
            var actorDirectory = sp.GetRequiredService<IActorDirectory>();
            var clusterMembership = sp.GetRequiredService<IQuarkClusterMembership>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartRoutingOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SmartRouter>>();
            return new SmartRouter(actorDirectory, clusterMembership, options, logger, localSiloId);
        });

        return services;
    }
}
