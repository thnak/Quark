namespace Quark.Placement.Abstractions;

/// <summary>
/// Represents information about a NUMA (Non-Uniform Memory Access) node.
/// </summary>
public sealed class NumaNodeInfo
{
    /// <summary>
    /// Gets or sets the NUMA node ID (0-based).
    /// </summary>
    public int NodeId { get; init; }

    /// <summary>
    /// Gets or sets the processor IDs (logical cores) associated with this NUMA node.
    /// </summary>
    public required IReadOnlyList<int> ProcessorIds { get; init; }

    /// <summary>
    /// Gets or sets the total memory capacity of this NUMA node in bytes.
    /// </summary>
    public long MemoryCapacityBytes { get; init; }

    /// <summary>
    /// Gets or sets the available (free) memory on this NUMA node in bytes.
    /// </summary>
    public long AvailableMemoryBytes { get; init; }

    /// <summary>
    /// Gets or sets the current CPU utilization percentage for this NUMA node (0-100).
    /// </summary>
    public double CpuUtilizationPercent { get; init; }

    /// <summary>
    /// Gets or sets the number of actors currently hosted on this NUMA node.
    /// </summary>
    public int ActiveActorCount { get; init; }
}
