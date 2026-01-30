using System.Diagnostics;
using Quark.Profiling.Abstractions;

namespace Quark.Profiling.Linux;

/// <summary>
/// Linux-specific implementation of hardware metrics collector.
/// Uses /proc filesystem for efficient metrics collection without external dependencies.
/// </summary>
public sealed class LinuxHardwareMetricsCollector : IHardwareMetricsCollector
{
    private readonly int _processId;
    private readonly int _processorCount;
    private long _lastCpuTime;
    private long _lastSystemCpuTime;
    private DateTime _lastCpuCheck;
    private long _lastNetworkBytesReceived;
    private long _lastNetworkBytesSent;
    private DateTime _lastNetworkCheck;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxHardwareMetricsCollector"/> class.
    /// </summary>
    public LinuxHardwareMetricsCollector()
    {
        _processId = Environment.ProcessId;
        _processorCount = Environment.ProcessorCount;
        _lastCpuCheck = DateTime.UtcNow;
        _lastNetworkCheck = DateTime.UtcNow;
        
        // Initialize baseline values
        Task.Run(async () =>
        {
            _lastCpuTime = await ReadProcessCpuTimeAsync(default);
            _lastSystemCpuTime = await ReadSystemCpuTimeAsync(default);
            var (received, sent) = await ReadNetworkStatsAsync(default);
            _lastNetworkBytesReceived = received;
            _lastNetworkBytesSent = sent;
        }).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<double> GetProcessCpuUsageAsync(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTime.UtcNow;
        var currentCpuTime = await ReadProcessCpuTimeAsync(cancellationToken);
        
        var timeDelta = (currentTime - _lastCpuCheck).TotalMilliseconds;
        if (timeDelta <= 0) return 0.0;

        var cpuDelta = currentCpuTime - _lastCpuTime;
        var cpuUsage = (cpuDelta / timeDelta) * 100.0 / _processorCount;

        _lastCpuTime = currentCpuTime;
        _lastCpuCheck = currentTime;

        return Math.Min(100.0, Math.Max(0.0, cpuUsage));
    }

    /// <inheritdoc/>
    public async Task<double> GetSystemCpuUsageAsync(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTime.UtcNow;
        var currentSystemCpuTime = await ReadSystemCpuTimeAsync(cancellationToken);
        
        var timeDelta = (currentTime - _lastCpuCheck).TotalMilliseconds;
        if (timeDelta <= 0) return 0.0;

        var cpuDelta = currentSystemCpuTime - _lastSystemCpuTime;
        var cpuUsage = (cpuDelta / timeDelta) * 100.0;

        _lastSystemCpuTime = currentSystemCpuTime;

        return Math.Min(100.0, Math.Max(0.0, cpuUsage));
    }

    /// <inheritdoc/>
    public Task<long> GetProcessMemoryUsageAsync(CancellationToken cancellationToken = default)
    {
        var process = Process.GetCurrentProcess();
        return Task.FromResult(process.WorkingSet64);
    }

    /// <inheritdoc/>
    public async Task<long> GetSystemMemoryAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
            foreach (var line in lines)
            {
                if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var availableKb))
                    {
                        return availableKb * 1024; // Convert KB to bytes
                    }
                }
            }
        }
        catch
        {
            // Fall back to GC info if /proc/meminfo is not accessible
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <inheritdoc/>
    public async Task<long> GetSystemMemoryTotalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var totalKb))
                    {
                        return totalKb * 1024; // Convert KB to bytes
                    }
                }
            }
        }
        catch
        {
            // Fall back to GC info if /proc/meminfo is not accessible
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <inheritdoc/>
    public Task<int> GetThreadCountAsync(CancellationToken cancellationToken = default)
    {
        var process = Process.GetCurrentProcess();
        return Task.FromResult(process.Threads.Count);
    }

    /// <inheritdoc/>
    public async Task<long> GetNetworkBytesReceivedPerSecondAsync(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTime.UtcNow;
        var (currentReceived, _) = await ReadNetworkStatsAsync(cancellationToken);
        
        var timeDelta = (currentTime - _lastNetworkCheck).TotalSeconds;
        if (timeDelta <= 0) return 0;

        var bytesDelta = currentReceived - _lastNetworkBytesReceived;
        var bytesPerSecond = (long)(bytesDelta / timeDelta);

        _lastNetworkBytesReceived = currentReceived;
        _lastNetworkCheck = currentTime;

        return Math.Max(0, bytesPerSecond);
    }

    /// <inheritdoc/>
    public async Task<long> GetNetworkBytesSentPerSecondAsync(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTime.UtcNow;
        var (_, currentSent) = await ReadNetworkStatsAsync(cancellationToken);
        
        var timeDelta = (currentTime - _lastNetworkCheck).TotalSeconds;
        if (timeDelta <= 0) return 0;

        var bytesDelta = currentSent - _lastNetworkBytesSent;
        var bytesPerSecond = (long)(bytesDelta / timeDelta);

        _lastNetworkBytesSent = currentSent;

        return Math.Max(0, bytesPerSecond);
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

    private async Task<long> ReadProcessCpuTimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var statFile = $"/proc/{_processId}/stat";
            var content = await File.ReadAllTextAsync(statFile, cancellationToken);
            var parts = content.Split(' ');
            
            // Fields 14 and 15 are utime and stime (user and system CPU time in clock ticks)
            if (parts.Length >= 15 &&
                long.TryParse(parts[13], out var utime) &&
                long.TryParse(parts[14], out var stime))
            {
                // Convert clock ticks to milliseconds (typical Linux has 100 ticks per second)
                var totalTicks = utime + stime;
                return totalTicks * 10; // 10ms per tick (100 ticks/sec)
            }
        }
        catch
        {
            // If we can't read /proc, fall back to managed API
        }

        var process = Process.GetCurrentProcess();
        return (long)process.TotalProcessorTime.TotalMilliseconds;
    }

    private async Task<long> ReadSystemCpuTimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
            if (lines.Length > 0 && lines[0].StartsWith("cpu ", StringComparison.Ordinal))
            {
                var parts = lines[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                long totalTime = 0;
                for (int i = 1; i < Math.Min(parts.Length, 8); i++)
                {
                    if (long.TryParse(parts[i], out var time))
                    {
                        totalTime += time;
                    }
                }
                return totalTime * 10; // Convert jiffies to milliseconds
            }
        }
        catch
        {
            // Fall back if /proc/stat is not accessible
        }

        return 0;
    }

    private async Task<(long Received, long Sent)> ReadNetworkStatsAsync(CancellationToken cancellationToken)
    {
        long totalReceived = 0;
        long totalSent = 0;

        try
        {
            var lines = await File.ReadAllLinesAsync("/proc/net/dev", cancellationToken);
            foreach (var line in lines.Skip(2)) // Skip header lines
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("lo:", StringComparison.Ordinal))
                    continue; // Skip loopback

                var colonIndex = trimmed.IndexOf(':');
                if (colonIndex < 0) continue;

                var stats = trimmed.Substring(colonIndex + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (stats.Length >= 9)
                {
                    if (long.TryParse(stats[0], out var received))
                        totalReceived += received;
                    if (long.TryParse(stats[8], out var sent))
                        totalSent += sent;
                }
            }
        }
        catch
        {
            // If we can't read network stats, return zeros
        }

        return (totalReceived, totalSent);
    }
}
