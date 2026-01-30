namespace Quark.Profiling.Abstractions;

/// <summary>
/// Provides an abstraction for collecting hardware metrics from the system.
/// Implementations are platform-specific (Linux, Windows).
/// </summary>
public interface IHardwareMetricsCollector
{
    /// <summary>
    /// Gets the CPU usage percentage for the current process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CPU usage percentage (0-100).</returns>
    Task<double> GetProcessCpuUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total CPU usage percentage for the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>System CPU usage percentage (0-100).</returns>
    Task<double> GetSystemCpuUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the memory usage in bytes for the current process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory usage in bytes.</returns>
    Task<long> GetProcessMemoryUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total available system memory in bytes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total available memory in bytes.</returns>
    Task<long> GetSystemMemoryAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total system memory in bytes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total system memory in bytes.</returns>
    Task<long> GetSystemMemoryTotalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of threads in the current process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Thread count.</returns>
    Task<int> GetThreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the network bytes received per second.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bytes received per second.</returns>
    Task<long> GetNetworkBytesReceivedPerSecondAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the network bytes sent per second.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bytes sent per second.</returns>
    Task<long> GetNetworkBytesSentPerSecondAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive hardware metrics snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hardware metrics snapshot.</returns>
    Task<HardwareMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken cancellationToken = default);
}
