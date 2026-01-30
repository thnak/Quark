namespace Quark.Placement.Abstractions;

/// <summary>
/// GPU device selection strategies for load balancing actors across GPUs.
/// </summary>
public enum GpuDeviceSelectionStrategy
{
    /// <summary>
    /// Prefer GPU with lowest compute utilization.
    /// </summary>
    LeastUtilized = 0,

    /// <summary>
    /// Prefer GPU with most available memory.
    /// </summary>
    LeastMemoryUsed = 1,

    /// <summary>
    /// Distribute actors evenly across GPUs using round-robin.
    /// </summary>
    RoundRobin = 2,

    /// <summary>
    /// Always use the first available GPU.
    /// </summary>
    FirstAvailable = 3
}
