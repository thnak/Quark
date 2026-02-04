namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents the result of a load test.
/// </summary>
public sealed class LoadTestResult
{
    /// <summary>
    /// Gets or sets the test identifier.
    /// </summary>
    public required string TestId { get; init; }

    /// <summary>
    /// Gets or sets the test start time.
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// Gets or sets the test end time.
    /// </summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages sent.
    /// </summary>
    public long TotalMessages { get; set; }

    /// <summary>
    /// Gets or sets the number of successful messages.
    /// </summary>
    public long SuccessfulMessages { get; set; }

    /// <summary>
    /// Gets or sets the number of failed messages.
    /// </summary>
    public long FailedMessages { get; set; }

    /// <summary>
    /// Gets or sets latency statistics.
    /// </summary>
    public LatencyStatistics Latency { get; set; } = new();

    /// <summary>
    /// Gets or sets the messages per second achieved.
    /// </summary>
    public double MessagesPerSecond { get; set; }

    /// <summary>
    /// Gets the test duration.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Gets the success rate.
    /// </summary>
    public double SuccessRate =>
        TotalMessages > 0 ? (SuccessfulMessages / (double)TotalMessages) * 100.0 : 0.0;
}