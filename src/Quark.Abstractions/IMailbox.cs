namespace Quark.Abstractions;

/// <summary>
/// Represents a mailbox for an actor that manages message queuing and processing.
/// </summary>
public interface IMailbox : IDisposable
{
    /// <summary>
    /// Gets the actor ID this mailbox belongs to.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Gets the current number of messages in the mailbox.
    /// </summary>
    int MessageCount { get; }

    /// <summary>
    /// Gets whether the mailbox is currently processing messages.
    /// </summary>
    bool IsProcessing { get; }

    /// <summary>
    /// Posts a message to the mailbox for processing.
    /// </summary>
    /// <param name="message">The message to post.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the message was posted successfully, false if the mailbox is full.</returns>
    ValueTask<bool> PostAsync(IActorMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts processing messages from the mailbox.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing messages from the mailbox.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
