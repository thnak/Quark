using Microsoft.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering NUMA optimization in the service collection.
/// </summary>
public static class NumaOptimizationExtensions
{
    /// <summary>
    /// Adds NUMA-aware actor placement optimization to the service collection.
    /// This enables intelligent actor placement based on NUMA topology to minimize
    /// memory access latency and maximize CPU cache efficiency.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for NUMA optimization.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// NUMA optimization requires a platform-specific implementation:
    /// - On Linux: Add Quark.Placement.Numa.Linux package
    /// - On Windows: Add Quark.Placement.Numa.Windows package
    /// 
    /// After calling this method, register the platform-specific implementation:
    /// <code>
    /// services.AddSingleton&lt;INumaPlacementStrategy, LinuxNumaPlacementStrategy&gt;();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddNumaOptimization(
        this IServiceCollection services,
        Action<NumaOptimizationOptions>? configureOptions = null)
    {
        // Register options
        var options = new NumaOptimizationOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        // Note: Platform-specific INumaPlacementStrategy implementation must be registered by the caller
        // This keeps the DI package lean and allows users to choose their implementation

        return services;
    }
}
