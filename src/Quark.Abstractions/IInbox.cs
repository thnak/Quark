namespace Quark.Abstractions;

/// <summary>
///     Interface for the Inbox pattern - ensures idempotent message processing.
///     Tracks processed message IDs to prevent duplicate processing.
/// </summary>
public interface IInbox
{
    /// <summary>
    ///     Checks if a message has already been processed.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="messageId">The unique message identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the message has been processed, false otherwise.</returns>
    Task<bool> IsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a message as processed to prevent duplicate handling.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="messageId">The unique message identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes old processed message IDs that are beyond the retention period.
    /// </summary>
    /// <param name="retentionPeriod">How long to keep processed message IDs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> CleanupOldEntriesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the timestamp when a message was processed.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="messageId">The unique message identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The timestamp when the message was processed, or null if not processed.</returns>
    Task<DateTimeOffset?> GetProcessedAtAsync(
        string actorId,
        string messageId,
        CancellationToken cancellationToken = default);
}
