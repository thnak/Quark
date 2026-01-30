using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// In-memory implementation of a dead letter queue for failed actor messages.
/// Thread-safe and suitable for single-silo scenarios or development/testing.
/// Supports retry policies with exponential backoff and message replay functionality.
/// </summary>
public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentDictionary<string, DeadLetterMessage> _messages = new();
    private readonly int _maxMessages;
    private readonly object _capacityLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryDeadLetterQueue"/> class.
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages to retain in the DLQ. Default is 10000.</param>
    public InMemoryDeadLetterQueue(int maxMessages = 10000)
    {
        if (maxMessages <= 0)
            throw new ArgumentException("Max messages must be greater than zero.", nameof(maxMessages));

        _maxMessages = maxMessages;
    }

    /// <inheritdoc />
    public int MessageCount => _messages.Count;

    /// <inheritdoc />
    public Task EnqueueAsync(IActorMessage message, string actorId, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        lock (_capacityLock)
        {
            // If at capacity, remove oldest message (FIFO)
            while (_messages.Count >= _maxMessages)
            {
                var oldestKey = _messages
                    .OrderBy(kvp => kvp.Value.EnqueuedAt)
                    .FirstOrDefault().Key;

                if (oldestKey != null)
                    _messages.TryRemove(oldestKey, out _);
                else
                    break; // Dictionary is empty
            }

            var deadLetterMessage = new DeadLetterMessage
            {
                Message = message,
                ActorId = actorId,
                Exception = exception,
                EnqueuedAt = DateTimeOffset.UtcNow,
                RetryCount = 0
            };

            // TryAdd might fail if duplicate MessageId exists, which is ok
            _messages.TryAdd(message.MessageId, deadLetterMessage);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeadLetterMessage>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var messages = _messages.Values
            .OrderByDescending(m => m.EnqueuedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(messages);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeadLetterMessage>> GetByActorAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));

        var messages = _messages.Values
            .Where(m => m.ActorId == actorId)
            .OrderByDescending(m => m.EnqueuedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(messages);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null or whitespace.", nameof(messageId));

        var removed = _messages.TryRemove(messageId, out _);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _messages.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ReplayAsync(string messageId, Func<string, IMailbox?> mailboxProvider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null or whitespace.", nameof(messageId));
        if (mailboxProvider == null)
            throw new ArgumentNullException(nameof(mailboxProvider));

        // Try to get the message
        if (!_messages.TryGetValue(messageId, out var deadLetterMessage))
            return false;

        // Get the mailbox for the actor
        var mailbox = mailboxProvider(deadLetterMessage.ActorId);
        if (mailbox == null)
            return false;

        // Try to replay the message
        try
        {
            var posted = await mailbox.PostAsync(deadLetterMessage.Message, cancellationToken);
            if (posted)
            {
                // Remove from DLQ after successful replay
                _messages.TryRemove(messageId, out _);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Replay failed, message stays in DLQ
            // Log the exception for debugging (in production, use ILogger)
            Console.WriteLine($"Failed to replay message {messageId}: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReplayBatchAsync(IEnumerable<string> messageIds, Func<string, IMailbox?> mailboxProvider, CancellationToken cancellationToken = default)
    {
        if (messageIds == null)
            throw new ArgumentNullException(nameof(messageIds));
        if (mailboxProvider == null)
            throw new ArgumentNullException(nameof(mailboxProvider));

        var replayed = new List<string>();

        foreach (var messageId in messageIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var success = await ReplayAsync(messageId, mailboxProvider, cancellationToken);
            if (success)
                replayed.Add(messageId);
        }

        return replayed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReplayByActorAsync(string actorId, Func<string, IMailbox?> mailboxProvider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));
        if (mailboxProvider == null)
            throw new ArgumentNullException(nameof(mailboxProvider));

        var messages = await GetByActorAsync(actorId, cancellationToken);
        var messageIds = messages.Select(m => m.Message.MessageId);

        return await ReplayBatchAsync(messageIds, mailboxProvider, cancellationToken);
    }
}
