namespace Quark.Abstractions;

/// <summary>
/// Configuration options for Dead Letter Queue specific to an actor type.
/// </summary>
public sealed class ActorTypeDeadLetterQueueOptions
{
    /// <summary>
    /// Gets or sets the actor type name this configuration applies to.
    /// </summary>
    public required string ActorTypeName { get; init; }

    /// <summary>
    /// Gets or sets whether the Dead Letter Queue is enabled for this actor type.
    /// If null, uses the global DLQ setting.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of messages to retain in the DLQ for this actor type.
    /// If null, uses the global MaxMessages setting.
    /// </summary>
    public int? MaxMessages { get; set; }

    /// <summary>
    /// Gets or sets whether to capture exception stack traces for this actor type.
    /// If null, uses the global CaptureStackTraces setting.
    /// </summary>
    public bool? CaptureStackTraces { get; set; }

    /// <summary>
    /// Gets or sets the retry policy for this actor type.
    /// If null, no retry is performed before sending to DLQ.
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }
}
