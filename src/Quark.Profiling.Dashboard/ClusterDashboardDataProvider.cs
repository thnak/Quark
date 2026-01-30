using Quark.Profiling.Abstractions;

namespace Quark.Profiling.Dashboard;

/// <summary>
/// Default implementation of cluster dashboard data provider.
/// Provides API data for cluster visualization.
/// </summary>
public sealed class ClusterDashboardDataProvider : IClusterDashboardDataProvider
{
    private readonly IActorProfiler _actorProfiler;
    private readonly IHardwareMetricsCollector? _hardwareMetricsCollector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterDashboardDataProvider"/> class.
    /// </summary>
    /// <param name="actorProfiler">Actor profiler for getting actor statistics.</param>
    /// <param name="hardwareMetricsCollector">Optional hardware metrics collector.</param>
    public ClusterDashboardDataProvider(
        IActorProfiler actorProfiler,
        IHardwareMetricsCollector? hardwareMetricsCollector = null)
    {
        _actorProfiler = actorProfiler;
        _hardwareMetricsCollector = hardwareMetricsCollector;
    }

    /// <inheritdoc/>
    public Task<ActorDistributionData> GetActorDistributionAsync(CancellationToken cancellationToken = default)
    {
        var allProfilingData = _actorProfiler.GetAllProfilingData();
        
        var data = new ActorDistributionData();
        
        // For single silo, use "local" as silo ID
        // In a real cluster implementation, this would come from cluster membership
        var siloId = "local";
        
        var actorsByType = allProfilingData.GroupBy(a => a.ActorType);
        
        foreach (var group in actorsByType)
        {
            var count = group.Count();
            data.ActorTypeDistribution[group.Key] = count;
            
            if (!data.ActorTypesPerSilo.ContainsKey(siloId))
            {
                data.ActorTypesPerSilo[siloId] = new Dictionary<string, int>();
            }
            data.ActorTypesPerSilo[siloId][group.Key] = count;
        }
        
        data.ActorCountPerSilo[siloId] = allProfilingData.Count();
        
        return Task.FromResult(data);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SiloResourceUtilization>> GetSiloResourcesAsync(CancellationToken cancellationToken = default)
    {
        var siloId = "local";
        var activeActors = _actorProfiler.GetAllProfilingData().Count();
        
        var utilization = new SiloResourceUtilization
        {
            SiloId = siloId,
            Timestamp = DateTimeOffset.UtcNow,
            ActiveActors = activeActors
        };

        if (_hardwareMetricsCollector != null)
        {
            var snapshot = await _hardwareMetricsCollector.GetMetricsSnapshotAsync(cancellationToken);
            utilization.CpuUsage = snapshot.ProcessCpuUsage;
            utilization.MemoryUsage = snapshot.ProcessMemoryUsage;
            utilization.MemoryTotal = snapshot.SystemMemoryTotal;
            utilization.ThreadCount = snapshot.ThreadCount;
            utilization.NetworkBytesReceivedPerSecond = snapshot.NetworkBytesReceivedPerSecond;
            utilization.NetworkBytesSentPerSecond = snapshot.NetworkBytesSentPerSecond;
        }

        return new[] { utilization };
    }

    /// <inheritdoc/>
    public Task<NetworkTrafficData> GetNetworkTrafficAsync(CancellationToken cancellationToken = default)
    {
        var data = new NetworkTrafficData
        {
            Timestamp = DateTimeOffset.UtcNow
        };

        // In a single-silo scenario, network traffic is minimal
        // Real cluster implementation would aggregate from transport layer
        var siloId = "local";
        data.PerSiloTraffic[siloId] = (0, 0);

        return Task.FromResult(data);
    }

    /// <inheritdoc/>
    public Task<PlacementEffectivenessData> GetPlacementEffectivenessAsync(CancellationToken cancellationToken = default)
    {
        var data = new PlacementEffectivenessData
        {
            Timestamp = DateTimeOffset.UtcNow,
            LoadDistributionScore = 100.0, // Perfect for single silo
            LocalityScore = 100.0, // All calls are local in single silo
            ActorCountStdDev = 0.0, // No distribution in single silo
            LocalCallRatio = 1.0 // 100% local calls in single silo
        };

        return Task.FromResult(data);
    }
}
