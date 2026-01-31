namespace Quark.Placement.Locality;

/// <summary>
/// Configuration options for locality-aware placement.
/// </summary>
public sealed class LocalityAwarePlacementOptions
{
    /// <summary>
    /// Gets or sets the time window for analyzing communication patterns.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the minimum message count threshold for considering actor pairs as "hot".
    /// Default: 100 messages.
    /// </summary>
    public long HotPairThreshold { get; set; } = 100;

    /// <summary>
    /// Gets or sets the weight for locality vs load balancing (0.0 = all load balance, 1.0 = all locality).
    /// Default: 0.7 (favor locality).
    /// </summary>
    public double LocalityWeight { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets how often to clean up old communication data.
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the maximum age of communication data to retain.
    /// Default: 30 minutes.
    /// </summary>
    public TimeSpan MaxDataAge { get; set; } = TimeSpan.FromMinutes(30);
}
