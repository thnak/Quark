using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding Quark silo health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for the Quark Silo.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="name">Optional health check name. Defaults to "quark_silo".</param>
    /// <param name="failureStatus">The health status to report when the check fails. Defaults to Unhealthy.</param>
    /// <param name="tags">Optional tags for categorizing the health check.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder AddHealthCheck(
        this IQuarkSiloBuilder builder,
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
            .AddCheck<QuarkSiloHealthCheck>(
                name ?? "quark_silo",
                failureStatus,
                tags ?? Array.Empty<string>());

        return builder;
    }
}
