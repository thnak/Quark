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