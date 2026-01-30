namespace Quark.Profiling.Abstractions;

/// <summary>
/// Provides load testing capabilities for Quark actors.
/// </summary>
public interface ILoadTestOrchestrator
{
    /// <summary>
    /// Starts a load test scenario.
    /// </summary>
    /// <param name="scenario">The load test scenario to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Load test result.</returns>
    Task<LoadTestResult> StartLoadTestAsync(LoadTestScenario scenario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a running load test.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns>Current load test status.</returns>
    LoadTestStatus? GetTestStatus(string testId);

    /// <summary>
    /// Stops a running load test.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopLoadTestAsync(string testId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a load test scenario.
/// </summary>
public sealed class LoadTestScenario
{
    /// <summary>
    /// Gets or sets the unique test identifier.
    /// </summary>
    public string TestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the actor type to test.
    /// </summary>
    public required string ActorType { get; init; }

    /// <summary>
    /// Gets or sets the number of concurrent actors.
    /// </summary>
    public int ConcurrentActors { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of messages per actor.
    /// </summary>
    public int MessagesPerActor { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the test duration in seconds (0 for message count based).
    /// </summary>
    public int DurationSeconds { get; set; } = 0;

    /// <summary>
    /// Gets or sets the message generation rate (messages per second, 0 for unlimited).
    /// </summary>
    public int MessageRateLimit { get; set; } = 0;

    /// <summary>
    /// Gets or sets the workload generator function name.
    /// </summary>
    public string? WorkloadGenerator { get; set; }
}

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

/// <summary>
/// Represents load test status.
/// </summary>
public sealed class LoadTestStatus
{
    /// <summary>
    /// Gets or sets the test identifier.
    /// </summary>
    public required string TestId { get; init; }

    /// <summary>
    /// Gets or sets the current test state.
    /// </summary>
    public LoadTestState State { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; set; }

    /// <summary>
    /// Gets or sets the number of messages processed so far.
    /// </summary>
    public long MessagesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the current messages per second rate.
    /// </summary>
    public double CurrentMessagesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// Represents load test state.
/// </summary>
public enum LoadTestState
{
    /// <summary>
    /// Test is being initialized.
    /// </summary>
    Initializing,

    /// <summary>
    /// Test is running.
    /// </summary>
    Running,

    /// <summary>
    /// Test is completing.
    /// </summary>
    Completing,

    /// <summary>
    /// Test completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Test failed with errors.
    /// </summary>
    Failed,

    /// <summary>
    /// Test was cancelled.
    /// </summary>
    Cancelled
}

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
