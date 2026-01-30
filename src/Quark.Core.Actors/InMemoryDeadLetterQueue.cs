using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// In-memory implementation of a dead letter queue for failed actor messages.
/// Thread-safe and suitable for single-silo scenarios or development/testing.
/// </summary>
public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentDictionary<string, DeadLetterMessage> _messages = new();
    private readonly int _maxMessages;

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

        // If at capacity, remove oldest message (FIFO)
        if (_messages.Count >= _maxMessages)
        {
            var oldestKey = _messages
                .OrderBy(kvp => kvp.Value.EnqueuedAt)
                .FirstOrDefault().Key;

            if (oldestKey != null)
                _messages.TryRemove(oldestKey, out _);
        }

        var deadLetterMessage = new DeadLetterMessage
        {
            Message = message,
            ActorId = actorId,
            Exception = exception,
            EnqueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        _messages.TryAdd(message.MessageId, deadLetterMessage);

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
}
