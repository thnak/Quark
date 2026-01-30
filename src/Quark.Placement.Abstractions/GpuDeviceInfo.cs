namespace Quark.Placement.Abstractions;

/// <summary>
/// Represents information about a GPU device.
/// </summary>
public sealed class GpuDeviceInfo
{
    /// <summary>
    /// Gets or sets the GPU device ID (0-based).
    /// </summary>
    public int DeviceId { get; init; }

    /// <summary>
    /// Gets or sets the name of the GPU device.
    /// </summary>
    public required string DeviceName { get; init; }

    /// <summary>
    /// Gets or sets the GPU vendor (e.g., "NVIDIA", "AMD", "Intel").
    /// </summary>
    public required string Vendor { get; init; }

    /// <summary>
    /// Gets or sets the GPU compute capability or driver version.
    /// </summary>
    public string? ComputeCapability { get; init; }

    /// <summary>
    /// Gets or sets the total memory capacity of the GPU in bytes.
    /// </summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>
    /// Gets or sets the available (free) memory on the GPU in bytes.
    /// </summary>
    public long AvailableMemoryBytes { get; init; }

    /// <summary>
    /// Gets or sets the current GPU utilization percentage (0-100).
    /// </summary>
    public double UtilizationPercent { get; init; }

    /// <summary>
    /// Gets or sets the current GPU temperature in Celsius.
    /// </summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>
    /// Gets or sets the number of actors currently using this GPU.
    /// </summary>
    public int ActiveActorCount { get; init; }

    /// <summary>
    /// Gets or sets whether the GPU supports the required compute features.
    /// </summary>
    public bool IsAvailable { get; init; }
}
