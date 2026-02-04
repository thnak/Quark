using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Health check for Quark Cluster Client to verify connectivity.
/// </summary>
public sealed class QuarkClientHealthCheck : IHealthCheck
{
    private readonly IClusterClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkClientHealthCheck"/> class.
    /// </summary>
    public QuarkClientHealthCheck(IClusterClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if we can reach the cluster membership
            var silos = await _client.ClusterMembership.GetActiveSilosAsync(cancellationToken);
            
            if (silos.Count == 0)
            {
                return HealthCheckResult.Degraded(
                    "No active silos available in the cluster");
            }

            var data = new Dictionary<string, object>
            {
                ["ClusterSize"] = silos.Count
            };

            return HealthCheckResult.Healthy(
                $"Connected to cluster with {silos.Count} active silos",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check cluster health",
                ex);
        }
    }
}