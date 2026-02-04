namespace Quark.Profiling.Abstractions;

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