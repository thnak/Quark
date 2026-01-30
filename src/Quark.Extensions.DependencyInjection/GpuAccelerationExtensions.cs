using Microsoft.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering GPU acceleration in the service collection.
/// </summary>
public static class GpuAccelerationExtensions
{
    /// <summary>
    /// Adds GPU acceleration for actor placement to the service collection.
    /// This enables actors performing compute-intensive operations (AI/ML, scientific computing)
    /// to be placed with affinity to specific GPU devices for optimal performance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for GPU acceleration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// GPU acceleration requires a hardware-specific implementation:
    /// - For NVIDIA GPUs: Add Quark.Placement.Gpu.Cuda package
    /// - For OpenCL: Add Quark.Placement.Gpu.OpenCL package (when available)
    /// 
    /// After calling this method, register the hardware-specific implementation:
    /// <code>
    /// services.AddSingleton&lt;IGpuPlacementStrategy, CudaGpuPlacementStrategy&gt;();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddGpuAcceleration(
        this IServiceCollection services,
        Action<GpuAccelerationOptions>? configureOptions = null)
    {
        // Register options
        var options = new GpuAccelerationOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        // Note: Hardware-specific IGpuPlacementStrategy implementation must be registered by the caller
        // This keeps the DI package lean and allows users to choose their implementation

        return services;
    }
}
