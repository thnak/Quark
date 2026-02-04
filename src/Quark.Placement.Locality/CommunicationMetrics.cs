namespace Quark.Placement.Locality;

/// <summary>
/// Represents communication metrics between two actors.
/// </summary>
public sealed class CommunicationMetrics
{
    /// <summary>
    /// Gets or sets the total number of messages exchanged.
    /// </summary>
    public long MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the total bytes transferred.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the average latency for message delivery.
    /// </summary>
    public TimeSpan AverageLatency { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last recorded interaction.
    /// </summary>
    public DateTimeOffset LastInteraction { get; set; }
}