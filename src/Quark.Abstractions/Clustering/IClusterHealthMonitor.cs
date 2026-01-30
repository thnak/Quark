namespace Quark.Abstractions.Clustering;

/// <summary>
///     Monitors cluster health and coordinates automatic silo eviction.
/// </summary>
public interface IClusterHealthMonitor
{
    /// <summary>
    ///     Gets the current eviction policy options.
    /// </summary>
    EvictionPolicyOptions Options { get; }

    /// <summary>
    ///     Starts health monitoring for the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops health monitoring for the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the health score for the current silo.
    /// </summary>
    /// <param name="healthScore">The health score to record.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpdateHealthScoreAsync(SiloHealthScore healthScore, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the health score for a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The health score, or null if not available.</returns>
    Task<SiloHealthScore?> GetHealthScoreAsync(string siloId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the historical health scores for a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="count">The maximum number of historical scores to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Historical health scores in chronological order (oldest first).</returns>
    Task<IReadOnlyList<SiloHealthScore>> GetHealthScoreHistoryAsync(
        string siloId,
        int count = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Manually triggers a health check for all silos.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task PerformHealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Event raised when a silo is evicted from the cluster.
    /// </summary>
    event EventHandler<SiloEvictedEventArgs>? SiloEvicted;

    /// <summary>
    ///     Event raised when a silo's health degrades.
    /// </summary>
    event EventHandler<SiloHealthDegradedEventArgs>? SiloHealthDegraded;
}

/// <summary>
///     Event arguments for silo eviction events.
/// </summary>
public sealed class SiloEvictedEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloEvictedEventArgs" /> class.
    /// </summary>
    public SiloEvictedEventArgs(SiloInfo siloInfo, string reason)
    {
        SiloInfo = siloInfo ?? throw new ArgumentNullException(nameof(siloInfo));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Gets the information about the evicted silo.
    /// </summary>
    public SiloInfo SiloInfo { get; }

    /// <summary>
    ///     Gets the reason for eviction.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    ///     Gets the timestamp when the eviction occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
///     Event arguments for silo health degradation events.
/// </summary>
public sealed class SiloHealthDegradedEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloHealthDegradedEventArgs" /> class.
    /// </summary>
    public SiloHealthDegradedEventArgs(
        SiloInfo siloInfo,
        SiloHealthScore healthScore,
        bool predictedFailure)
    {
        SiloInfo = siloInfo ?? throw new ArgumentNullException(nameof(siloInfo));
        HealthScore = healthScore ?? throw new ArgumentNullException(nameof(healthScore));
        PredictedFailure = predictedFailure;
    }

    /// <summary>
    ///     Gets the information about the silo with degraded health.
    /// </summary>
    public SiloInfo SiloInfo { get; }

    /// <summary>
    ///     Gets the current health score.
    /// </summary>
    public SiloHealthScore HealthScore { get; }

    /// <summary>
    ///     Gets a value indicating whether a failure is predicted.
    /// </summary>
    public bool PredictedFailure { get; }
}
