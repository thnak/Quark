namespace Quark.Placement.Memory;

/// <summary>
/// Configuration options for memory-aware placement.
/// </summary>
public sealed class MemoryAwarePlacementOptions
{
    /// <summary>
    /// Gets or sets the warning threshold in bytes.
    /// When memory usage exceeds this, warnings are logged.
    /// Default: 1 GB.
    /// </summary>
    public long WarningThresholdBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the critical threshold in bytes.
    /// When memory usage exceeds this, rebalancing is triggered.
    /// Default: 1.5 GB.
    /// </summary>
    public long CriticalThresholdBytes { get; set; } = 1536L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the memory pressure threshold (0.0 to 1.0).
    /// When pressure exceeds this, placement prefers silos with lower memory usage.
    /// Default: 0.7 (70% utilization).
    /// </summary>
    public double MemoryPressureThreshold { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets the memory reservation percentage.
    /// This percentage of memory is reserved as a safety buffer.
    /// Default: 0.2 (20% reserve).
    /// </summary>
    public double MemoryReservationPercentage { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets whether to reject actor placement if memory is critical.
    /// Default: true.
    /// </summary>
    public bool RejectPlacementOnCriticalMemory { get; set; } = true;
}
