namespace Quark.Abstractions.Clustering;

/// <summary>
/// Provides membership management for a Quark cluster.
/// </summary>
public interface IClusterMembership
{
    /// <summary>
    /// Gets the current silo ID.
    /// </summary>
    string CurrentSiloId { get; }

    /// <summary>
    /// Registers this silo in the cluster.
    /// </summary>
    /// <param name="siloInfo">Information about this silo.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters this silo from the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UnregisterSiloAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active silos in the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of active silos.</returns>
    Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The silo information, or null if not found.</returns>
    Task<SiloInfo?> GetSiloAsync(string siloId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat timestamp for this silo.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts monitoring cluster membership changes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops monitoring cluster membership changes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a silo joins the cluster.
    /// </summary>
    event EventHandler<SiloInfo>? SiloJoined;

    /// <summary>
    /// Event raised when a silo leaves the cluster.
    /// </summary>
    event EventHandler<SiloInfo>? SiloLeft;
}
