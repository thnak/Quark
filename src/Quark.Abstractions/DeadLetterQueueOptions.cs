namespace Quark.Abstractions;

/// <summary>
/// Configuration options for the Dead Letter Queue.
/// </summary>
public sealed class DeadLetterQueueOptions
{
    /// <summary>
    /// Gets or sets whether the Dead Letter Queue is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages to retain in the DLQ.
    /// Older messages are removed when this limit is reached (FIFO).
    /// Default is 10000.
    /// </summary>
    public int MaxMessages { get; set; } = 10000;

    /// <summary>
    /// Gets or sets whether to capture exception stack traces.
    /// Disabling this can save memory for high-volume failures.
    /// Default is true.
    /// </summary>
    public bool CaptureStackTraces { get; set; } = true;
}
