namespace Quark.Abstractions.Clustering;

/// <summary>
///     Represents health metrics and score for a silo in the cluster.
/// </summary>
public sealed class SiloHealthScore
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloHealthScore" /> class.
    /// </summary>
    public SiloHealthScore(
        double cpuUsagePercent,
        double memoryUsagePercent,
        double networkLatencyMs,
        DateTimeOffset timestamp)
    {
        CpuUsagePercent = Math.Clamp(cpuUsagePercent, 0, 100);
        MemoryUsagePercent = Math.Clamp(memoryUsagePercent, 0, 100);
        NetworkLatencyMs = Math.Max(0, networkLatencyMs);
        Timestamp = timestamp;
    }

    /// <summary>
    ///     Gets the CPU usage percentage (0-100).
    /// </summary>
    public double CpuUsagePercent { get; }

    /// <summary>
    ///     Gets the memory usage percentage (0-100).
    /// </summary>
    public double MemoryUsagePercent { get; }

    /// <summary>
    ///     Gets the network latency in milliseconds.
    /// </summary>
    public double NetworkLatencyMs { get; }

    /// <summary>
    ///     Gets the timestamp when this health score was recorded.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     Gets the overall health score (0-100, higher is healthier).
    ///     This is calculated using a weighted formula.
    /// </summary>
    public double OverallScore => CalculateOverallScore();

    private double CalculateOverallScore()
    {
        // Invert CPU and memory (lower usage = healthier)
        var cpuScore = 100 - CpuUsagePercent;
        var memoryScore = 100 - MemoryUsagePercent;
        
        // Convert latency to score (lower latency = healthier)
        // Assume 0ms = 100, 1000ms = 0
        var latencyScore = Math.Max(0, 100 - (NetworkLatencyMs / 10));
        
        // Weighted average: CPU 30%, Memory 30%, Latency 40%
        return (cpuScore * 0.3) + (memoryScore * 0.3) + (latencyScore * 0.4);
    }

    /// <summary>
    ///     Determines if this health score indicates a healthy silo.
    /// </summary>
    /// <param name="threshold">The minimum health score threshold (0-100).</param>
    /// <returns>True if the silo is healthy; otherwise, false.</returns>
    public bool IsHealthy(double threshold = 50.0)
    {
        return OverallScore >= threshold;
    }
}
