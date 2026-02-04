namespace Quark.Abstractions;

/// <summary>
/// Represents a message in the dead letter queue with failure metadata.
/// </summary>
public sealed class DeadLetterMessage
{
    /// <summary>
    /// Gets or sets the original message that failed.
    /// </summary>
    public required IActorMessage Message { get; init; }

    /// <summary>
    /// Gets or sets the ID of the actor that failed to process the message.
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>
    /// Gets or sets the exception that caused the failure.
    /// </summary>
    public required Exception Exception { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the message was added to the DLQ.
    /// </summary>
    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>
    /// Gets or sets the number of retry attempts (if retry policy is configured).
    /// </summary>
    public int RetryCount { get; init; }
}