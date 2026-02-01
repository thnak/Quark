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
    private readonly IClusterHealthMonitor? _healthMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkSiloHealthCheck"/> class.
    /// </summary>
    public QuarkSiloHealthCheck(
        IQuarkSilo silo, 
        IQuarkClusterMembership clusterMembership,
        IClusterHealthMonitor? healthMonitor = null)
    {
        _silo = silo ?? throw new ArgumentNullException(nameof(silo));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _healthMonitor = healthMonitor;
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

            // Include health score if monitoring is enabled
            if (_healthMonitor != null)
            {
                var healthScore = await _healthMonitor.GetHealthScoreAsync(_silo.SiloId, cancellationToken);
                if (healthScore != null)
                {
                    data["HealthScore"] = healthScore.OverallScore;
                    data["CpuUsage"] = healthScore.CpuUsagePercent;
                    data["MemoryUsage"] = healthScore.MemoryUsagePercent;
                    data["NetworkLatency"] = healthScore.NetworkLatencyMs;
                    
                    // Return degraded if health score is low but not critical
                    if (healthScore.OverallScore < 50 && healthScore.OverallScore >= 30)
                    {
                        return HealthCheckResult.Degraded(
                            $"Silo {_silo.SiloId} health is degraded (score: {healthScore.OverallScore:F1})",
                            data: data);
                    }
                    
                    if (healthScore.OverallScore < 30)
                    {
                        return HealthCheckResult.Unhealthy(
                            $"Silo {_silo.SiloId} health is critical (score: {healthScore.OverallScore:F1})",
                            data: data);
                    }
                }
            }

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

