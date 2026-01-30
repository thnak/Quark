namespace Quark.Profiling.Abstractions;

/// <summary>
/// Provides cluster-wide dashboard data for visualization.
/// This is an API-only interface - UI implementation is left to users.
/// </summary>
public interface IClusterDashboardDataProvider
{
    /// <summary>
    /// Gets actor distribution across silos (heat map data).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Actor distribution data.</returns>
    Task<ActorDistributionData> GetActorDistributionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets resource utilization for all silos.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of silo resource utilization.</returns>
    Task<IEnumerable<SiloResourceUtilization>> GetSiloResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets network traffic patterns across the cluster.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Network traffic data.</returns>
    Task<NetworkTrafficData> GetNetworkTrafficAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets placement policy effectiveness metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Placement effectiveness data.</returns>
    Task<PlacementEffectivenessData> GetPlacementEffectivenessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents actor distribution data across silos.
/// </summary>
public sealed class ActorDistributionData
{
    /// <summary>
    /// Gets or sets the timestamp of the data.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets actor counts per silo.
    /// Key: SiloId, Value: Actor count
    /// </summary>
    public Dictionary<string, int> ActorCountPerSilo { get; set; } = new();

    /// <summary>
    /// Gets or sets actor type distribution.
    /// Key: ActorType, Value: Count across all silos
    /// </summary>
    public Dictionary<string, int> ActorTypeDistribution { get; set; } = new();

    /// <summary>
    /// Gets or sets detailed per-silo actor type breakdown.
    /// Key: SiloId, Value: Dictionary of ActorType to Count
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> ActorTypesPerSilo { get; set; } = new();
}

/// <summary>
/// Represents resource utilization for a single silo.
/// </summary>
public sealed class SiloResourceUtilization
{
    /// <summary>
    /// Gets or sets the silo identifier.
    /// </summary>
    public required string SiloId { get; init; }

    /// <summary>
    /// Gets or sets the timestamp of the data.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets CPU usage percentage.
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// Gets or sets memory usage in bytes.
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Gets or sets total available memory in bytes.
    /// </summary>
    public long MemoryTotal { get; set; }

    /// <summary>
    /// Gets or sets the number of active actors.
    /// </summary>
    public int ActiveActors { get; set; }

    /// <summary>
    /// Gets or sets the number of threads.
    /// </summary>
    public int ThreadCount { get; set; }

    /// <summary>
    /// Gets or sets network bytes received per second.
    /// </summary>
    public long NetworkBytesReceivedPerSecond { get; set; }

    /// <summary>
    /// Gets or sets network bytes sent per second.
    /// </summary>
    public long NetworkBytesSentPerSecond { get; set; }

    /// <summary>
    /// Gets memory usage percentage.
    /// </summary>
    public double MemoryUsagePercent =>
        MemoryTotal > 0 ? (MemoryUsage / (double)MemoryTotal) * 100.0 : 0.0;
}

/// <summary>
/// Represents network traffic patterns in the cluster.
/// </summary>
public sealed class NetworkTrafficData
{
    /// <summary>
    /// Gets or sets the timestamp of the data.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets total bytes sent per second across cluster.
    /// </summary>
    public long TotalBytesSentPerSecond { get; set; }

    /// <summary>
    /// Gets or sets total bytes received per second across cluster.
    /// </summary>
    public long TotalBytesReceivedPerSecond { get; set; }

    /// <summary>
    /// Gets or sets per-silo network traffic.
    /// Key: SiloId, Value: (BytesSent, BytesReceived) per second
    /// </summary>
    public Dictionary<string, (long Sent, long Received)> PerSiloTraffic { get; set; } = new();

    /// <summary>
    /// Gets or sets inter-silo communication patterns.
    /// Key: "SourceSilo->TargetSilo", Value: BytesPerSecond
    /// </summary>
    public Dictionary<string, long> InterSiloCommunication { get; set; } = new();
}

/// <summary>
/// Represents placement policy effectiveness metrics.
/// </summary>
public sealed class PlacementEffectivenessData
{
    /// <summary>
    /// Gets or sets the timestamp of the data.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets load distribution score (0-100, higher is better).
    /// Measures how evenly actors are distributed.
    /// </summary>
    public double LoadDistributionScore { get; set; }

    /// <summary>
    /// Gets or sets locality score (0-100, higher is better).
    /// Measures how well related actors are co-located.
    /// </summary>
    public double LocalityScore { get; set; }

    /// <summary>
    /// Gets or sets the standard deviation of actor counts across silos.
    /// Lower is better (more even distribution).
    /// </summary>
    public double ActorCountStdDev { get; set; }

    /// <summary>
    /// Gets or sets the ratio of local calls to remote calls.
    /// Higher is better (more local calls).
    /// </summary>
    public double LocalCallRatio { get; set; }
}
