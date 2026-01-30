namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents a snapshot of hardware metrics at a point in time.
/// </summary>
public sealed class HardwareMetricsSnapshot
{
    /// <summary>
    /// Gets or sets the timestamp when the snapshot was taken.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the process CPU usage percentage (0-100).
    /// </summary>
    public double ProcessCpuUsage { get; set; }

    /// <summary>
    /// Gets or sets the system CPU usage percentage (0-100).
    /// </summary>
    public double SystemCpuUsage { get; set; }

    /// <summary>
    /// Gets or sets the process memory usage in bytes.
    /// </summary>
    public long ProcessMemoryUsage { get; set; }

    /// <summary>
    /// Gets or sets the available system memory in bytes.
    /// </summary>
    public long SystemMemoryAvailable { get; set; }

    /// <summary>
    /// Gets or sets the total system memory in bytes.
    /// </summary>
    public long SystemMemoryTotal { get; set; }

    /// <summary>
    /// Gets or sets the thread count.
    /// </summary>
    public int ThreadCount { get; set; }

    /// <summary>
    /// Gets or sets the network bytes received per second.
    /// </summary>
    public long NetworkBytesReceivedPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the network bytes sent per second.
    /// </summary>
    public long NetworkBytesSentPerSecond { get; set; }

    /// <summary>
    /// Gets the system memory usage percentage.
    /// </summary>
    public double SystemMemoryUsagePercent =>
        SystemMemoryTotal > 0
            ? ((SystemMemoryTotal - SystemMemoryAvailable) / (double)SystemMemoryTotal) * 100.0
            : 0.0;
}
