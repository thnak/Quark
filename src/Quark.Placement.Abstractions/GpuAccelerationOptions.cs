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
    /// Gets or sets the preferred GPU backend.
    /// Default is <see cref="GpuBackend.Auto"/> which auto-detects the best available backend.
    /// </summary>
    public GpuBackend PreferredBackend { get; set; } = GpuBackend.Auto;

    /// <summary>
    /// Gets or sets the set of actor types that should use GPU acceleration.
    /// If null or empty, all actors marked with [GpuBound] attribute will be accelerated.
    /// Use the source-generated {AssemblyName}AcceleratedActorTypes.All property for easy configuration.
    /// </summary>
    /// <remarks>
    /// Example: options.AcceleratedActorTypes = MyAssemblyAcceleratedActorTypes.All;
    /// </remarks>
    public IReadOnlySet<Type>? AcceleratedActorTypes { get; set; }

    /// <summary>
    /// Gets or sets the GPU device selection strategy.
    /// Default is <see cref="GpuDeviceSelectionStrategy.LeastUtilized"/>.
    /// </summary>
    public GpuDeviceSelectionStrategy DeviceSelectionStrategy { get; set; } = GpuDeviceSelectionStrategy.LeastUtilized;

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
