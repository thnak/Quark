using System.Diagnostics;
using Quark.Profiling.Abstractions;

namespace Quark.Profiling.Windows;

/// <summary>
/// Windows-specific implementation of hardware metrics collector.
/// Uses Process and PerformanceCounter APIs for metrics collection.
/// Note: Some features may have limited functionality compared to Linux implementation.
/// </summary>
public sealed class WindowsHardwareMetricsCollector : IHardwareMetricsCollector
{
    private readonly Process _process;
    private readonly int _processorCount;
    private DateTime _lastCpuCheck;
    private TimeSpan _lastTotalProcessorTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsHardwareMetricsCollector"/> class.
    /// </summary>
    public WindowsHardwareMetricsCollector()
    {
        _process = Process.GetCurrentProcess();
        _processorCount = Environment.ProcessorCount;
        _lastCpuCheck = DateTime.UtcNow;
        _lastTotalProcessorTime = _process.TotalProcessorTime;
    }

    /// <inheritdoc/>
    public Task<double> GetProcessCpuUsageAsync(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTime.UtcNow;
        var currentTotalProcessorTime = _process.TotalProcessorTime;

        var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
        var totalMsPassed = (currentTime - _lastCpuCheck).TotalMilliseconds;

        if (totalMsPassed <= 0)
            return Task.FromResult(0.0);

        var cpuUsageTotal = cpuUsedMs / totalMsPassed * 100.0;
        var cpuUsagePerCore = cpuUsageTotal / _processorCount;

        _lastTotalProcessorTime = currentTotalProcessorTime;
        _lastCpuCheck = currentTime;

        return Task.FromResult(Math.Min(100.0, Math.Max(0.0, cpuUsagePerCore)));
    }

    /// <inheritdoc/>
    public Task<double> GetSystemCpuUsageAsync(CancellationToken cancellationToken = default)
    {
        // Windows: Estimating system CPU usage is complex without performance counters
        // For AOT compatibility, we'll use a simplified approach based on process CPU
        // In a real implementation, you might want to use WMI or performance counters
        // but those have AOT compatibility challenges
        
        // Return 0 for now - full implementation would require platform-specific APIs
        // Users can use OpenTelemetry or other monitoring solutions for system-wide metrics
        return Task.FromResult(0.0);
    }

    /// <inheritdoc/>
    public Task<long> GetProcessMemoryUsageAsync(CancellationToken cancellationToken = default)
    {
        _process.Refresh();
        return Task.FromResult(_process.WorkingSet64);
    }

    /// <inheritdoc/>
    public Task<long> GetSystemMemoryAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Use GC memory info which works cross-platform
        var memoryInfo = GC.GetGCMemoryInfo();
        return Task.FromResult(memoryInfo.TotalAvailableMemoryBytes);
    }

    /// <inheritdoc/>
    public Task<long> GetSystemMemoryTotalAsync(CancellationToken cancellationToken = default)
    {
        // Windows: Get total physical memory
        // For AOT compatibility, we use GC memory info
        var memoryInfo = GC.GetGCMemoryInfo();
        return Task.FromResult(memoryInfo.TotalAvailableMemoryBytes);
    }

    /// <inheritdoc/>
    public Task<int> GetThreadCountAsync(CancellationToken cancellationToken = default)
    {
        _process.Refresh();
        return Task.FromResult(_process.Threads.Count);
    }

    /// <inheritdoc/>
    public Task<long> GetNetworkBytesReceivedPerSecondAsync(CancellationToken cancellationToken = default)
    {
        // Windows: Network metrics would typically require performance counters
        // For AOT compatibility and simplicity, returning 0
        // Production implementations should integrate with system monitoring tools
        return Task.FromResult(0L);
    }

    /// <inheritdoc/>
    public Task<long> GetNetworkBytesSentPerSecondAsync(CancellationToken cancellationToken = default)
    {
        // Windows: Network metrics would typically require performance counters
        // For AOT compatibility and simplicity, returning 0
        // Production implementations should integrate with system monitoring tools
        return Task.FromResult(0L);
    }

    /// <inheritdoc/>
    public async Task<HardwareMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return new HardwareMetricsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProcessCpuUsage = await GetProcessCpuUsageAsync(cancellationToken),
            SystemCpuUsage = await GetSystemCpuUsageAsync(cancellationToken),
            ProcessMemoryUsage = await GetProcessMemoryUsageAsync(cancellationToken),
            SystemMemoryAvailable = await GetSystemMemoryAvailableAsync(cancellationToken),
            SystemMemoryTotal = await GetSystemMemoryTotalAsync(cancellationToken),
            ThreadCount = await GetThreadCountAsync(cancellationToken),
            NetworkBytesReceivedPerSecond = await GetNetworkBytesReceivedPerSecondAsync(cancellationToken),
            NetworkBytesSentPerSecond = await GetNetworkBytesSentPerSecondAsync(cancellationToken)
        };
    }
}
