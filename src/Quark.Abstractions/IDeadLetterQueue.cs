namespace Quark.Abstractions;

/// <summary>
/// Represents a dead letter queue for capturing failed actor messages.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Gets the total number of messages in the dead letter queue.
    /// </summary>
    int MessageCount { get; }

    /// <summary>
    /// Enqueues a failed message to the dead letter queue.
    /// </summary>
    /// <param name="message">The message that failed.</param>
    /// <param name="actorId">The ID of the actor that failed to process the message.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task EnqueueAsync(IActorMessage message, string actorId, Exception exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all messages in the dead letter queue.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of dead letter messages.</returns>
    Task<IReadOnlyList<DeadLetterMessage>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves messages from the dead letter queue for a specific actor.
    /// </summary>
    /// <param name="actorId">The actor ID to filter by.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of dead letter messages for the specified actor.</returns>
    Task<IReadOnlyList<DeadLetterMessage>> GetByActorAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a message from the dead letter queue.
    /// </summary>
    /// <param name="messageId">The ID of the message to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the message was removed, false if not found.</returns>
    Task<bool> RemoveAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all messages from the dead letter queue.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

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
