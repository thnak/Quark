using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quark.Abstractions.Clustering;
using Quark.Hosting;
using Quark.Networking.Abstractions;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Health check for Quark Silo to verify it is active and operational.
/// </summary>
public sealed class QuarkSiloHealthCheck : IHealthCheck
{
    private readonly IQuarkSilo _silo;
    private readonly IQuarkClusterMembership _clusterMembership;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkSiloHealthCheck"/> class.
    /// </summary>
    public QuarkSiloHealthCheck(IQuarkSilo silo, IQuarkClusterMembership clusterMembership)
    {
        _silo = silo ?? throw new ArgumentNullException(nameof(silo));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if silo is in Active status
            if (_silo.Status != SiloStatus.Active)
            {
                return HealthCheckResult.Unhealthy(
                    $"Silo is not active. Current status: {_silo.Status}");
            }

            // Check if we can reach the cluster membership
            var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["SiloId"] = _silo.SiloId,
                ["Status"] = _silo.Status.ToString(),
                ["ActiveActors"] = _silo.GetActiveActors().Count,
                ["ClusterSize"] = silos.Count
            };

            return HealthCheckResult.Healthy(
                $"Silo {_silo.SiloId} is active with {data["ActiveActors"]} actors",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Health check failed for silo {_silo.SiloId}",
                ex);
        }
    }
}

/// <summary>
/// Health check for Quark Cluster Client to verify connectivity.
/// </summary>
public sealed class QuarkClientHealthCheck : IHealthCheck
{
    private readonly Client.IClusterClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkClientHealthCheck"/> class.
    /// </summary>
    public QuarkClientHealthCheck(Client.IClusterClient client)
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
