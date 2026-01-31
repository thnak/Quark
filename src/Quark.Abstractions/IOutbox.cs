namespace Quark.Abstractions;

/// <summary>
///     Interface for the Outbox pattern - ensures transactional message delivery.
///     Messages are stored in the outbox as part of the same transaction as state changes,
///     guaranteeing at-least-once delivery semantics.
/// </summary>
public interface IOutbox
{
    /// <summary>
    ///     Enqueues a message in the outbox for later delivery.
    ///     This should be called within a transaction along with state updates.
    /// </summary>
    /// <param name="message">The message to enqueue.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves pending messages that need to be sent.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of pending outbox messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a message as successfully sent.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsSentAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a message as failed and increments its retry count.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="error">The error that occurred.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsFailedAsync(string messageId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes messages that have been successfully sent and are older than the retention period.
    /// </summary>
    /// <param name="retentionPeriod">How long to keep sent messages.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of messages deleted.</returns>
    Task<int> CleanupSentMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}
