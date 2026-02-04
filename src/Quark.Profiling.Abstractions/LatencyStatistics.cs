namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents latency statistics with percentiles.
/// </summary>
public sealed class LatencyStatistics
{
    /// <summary>
    /// Gets or sets the minimum latency in milliseconds.
    /// </summary>
    public double MinMs { get; set; }

    /// <summary>
    /// Gets or sets the maximum latency in milliseconds.
    /// </summary>
    public double MaxMs { get; set; }

    /// <summary>
    /// Gets or sets the average (mean) latency in milliseconds.
    /// </summary>
    public double MeanMs { get; set; }

    /// <summary>
    /// Gets or sets the median (p50) latency in milliseconds.
    /// </summary>
    public double P50Ms { get; set; }

    /// <summary>
    /// Gets or sets the 95th percentile latency in milliseconds.
    /// </summary>
    public double P95Ms { get; set; }

    /// <summary>
    /// Gets or sets the 99th percentile latency in milliseconds.
    /// </summary>
    public double P99Ms { get; set; }

    /// <summary>
    /// Gets or sets the 99.9th percentile latency in milliseconds.
    /// </summary>
    public double P999Ms { get; set; }

    /// <summary>
    /// Gets or sets the standard deviation of latencies.
    /// </summary>
    public double StdDevMs { get; set; }
}