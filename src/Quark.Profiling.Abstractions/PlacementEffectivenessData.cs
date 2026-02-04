namespace Quark.Profiling.Abstractions;

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