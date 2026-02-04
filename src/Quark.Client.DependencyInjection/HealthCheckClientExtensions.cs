using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quark.Client;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Extension methods for adding Quark cluster client health checks.
/// </summary>
public static class HealthCheckClientExtensions
{
    /// <summary>
    /// Adds a health check for the Quark Cluster Client.
    /// </summary>
    /// <param name="builder">The client builder.</param>
    /// <param name="name">Optional health check name. Defaults to "quark_client".</param>
    /// <param name="failureStatus">The health status to report when the check fails. Defaults to Unhealthy.</param>
    /// <param name="tags">Optional tags for categorizing the health check.</param>
    /// <returns>The builder for chaining.</returns>
    public static IClusterClientBuilder AddHealthCheck(
        this IClusterClientBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services
            .AddHealthChecks()
            .AddCheck<QuarkClientHealthCheck>(
                name ?? "quark_client",
                failureStatus,
                tags ?? Array.Empty<string>());

        return builder;
    }
}