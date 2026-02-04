namespace Quark.Profiling.Abstractions;

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