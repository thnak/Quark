using Microsoft.Extensions.DependencyInjection;
using Quark.Profiling.Abstractions;

namespace Quark.Profiling.Dashboard;

/// <summary>
/// Extension methods for configuring Quark profiling services.
/// </summary>
public static class ProfilingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Quark profiling services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureHardwareCollector">Optional callback to configure hardware metrics collector.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQuarkProfiling(
        this IServiceCollection services,
        Action<IServiceCollection>? configureHardwareCollector = null)
    {
        // Register actor profiler as singleton
        services.AddSingleton<IActorProfiler, ActorProfiler>();

        // Register dashboard data provider
        services.AddSingleton<IClusterDashboardDataProvider, ClusterDashboardDataProvider>();

        // Allow custom hardware collector configuration
        configureHardwareCollector?.Invoke(services);

        return services;
    }

    /// <summary>
    /// Adds Linux hardware metrics collector.
    /// Only works on Linux systems.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLinuxHardwareMetrics(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux hardware metrics collector requires Linux OS.");
        }

        services.AddSingleton<IHardwareMetricsCollector>(sp =>
            new Linux.LinuxHardwareMetricsCollector());

        return services;
    }

    /// <summary>
    /// Adds Windows hardware metrics collector.
    /// Only works on Windows systems.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWindowsHardwareMetrics(this IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows hardware metrics collector requires Windows OS.");
        }

        services.AddSingleton<IHardwareMetricsCollector>(sp =>
            new Windows.WindowsHardwareMetricsCollector());

        return services;
    }

    /// <summary>
    /// Adds platform-specific hardware metrics collector automatically.
    /// Detects the current OS and registers the appropriate collector.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlatformHardwareMetrics(this IServiceCollection services)
    {
        if (OperatingSystem.IsLinux())
        {
            return services.AddLinuxHardwareMetrics();
        }
        else if (OperatingSystem.IsWindows())
        {
            return services.AddWindowsHardwareMetrics();
        }
        else
        {
            // For other platforms, don't register a hardware collector
            // The dashboard will work but without hardware metrics
            return services;
        }
    }
}
