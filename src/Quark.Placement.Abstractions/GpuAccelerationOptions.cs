namespace Quark.Placement.Abstractions;

/// <summary>
/// Configuration options for GPU-accelerated actor placement.
/// </summary>
public sealed class GpuAccelerationOptions
{
    /// <summary>
    /// Gets or sets whether GPU acceleration is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the preferred GPU backend ("cuda", "opencl", "auto").
    /// Default is "auto" which auto-detects the best available backend.
    /// </summary>
    public string PreferredBackend { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the list of actor types that should use GPU acceleration.
    /// If empty, all actors that request GPU will be accelerated.
    /// </summary>
    public List<string> AcceleratedActorTypes { get; set; } = new();

    /// <summary>
    /// Gets or sets the GPU device selection strategy.
    /// - "LeastUtilized": Prefer GPU with lowest utilization
    /// - "LeastMemoryUsed": Prefer GPU with most available memory
    /// - "RoundRobin": Distribute actors evenly across GPUs
    /// - "FirstAvailable": Always use the first available GPU
    /// Default is "LeastUtilized".
    /// </summary>
    public string DeviceSelectionStrategy { get; set; } = "LeastUtilized";

    /// <summary>
    /// Gets or sets the maximum GPU memory utilization percentage (0-1) before considering a device as full.
    /// Default is 0.90 (90%).
    /// </summary>
    public double MaxGpuMemoryUtilization { get; set; } = 0.90;

    /// <summary>
    /// Gets or sets the maximum GPU compute utilization percentage (0-1) before preferring another device.
    /// Default is 0.85 (85%).
    /// </summary>
    public double MaxGpuComputeUtilization { get; set; } = 0.85;

    /// <summary>
    /// Gets or sets whether to allow CPU fallback when no GPU is available.
    /// Default is true.
    /// </summary>
    public bool AllowCpuFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for refreshing GPU device metrics (in seconds).
    /// Default is 2 seconds.
    /// </summary>
    public int MetricsRefreshIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the minimum GPU compute capability required.
    /// For CUDA, this would be like "6.0" for Pascal or later.
    /// </summary>
    public string? MinimumComputeCapability { get; set; }

    /// <summary>
    /// Gets or sets whether to enable GPU memory pooling for efficient memory reuse.
    /// Default is true.
    /// </summary>
    public bool EnableMemoryPooling { get; set; } = true;
}
