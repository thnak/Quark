namespace Quark.Profiling.Abstractions;

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