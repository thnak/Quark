namespace Quark.Profiling.Abstractions;

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