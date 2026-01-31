using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Messaging;

/// <summary>
///     In-memory implementation of the outbox pattern for development and testing.
///     This implementation is NOT suitable for production use as it does not persist
///     messages across restarts.
/// </summary>
public sealed class InMemoryOutbox : IOutbox
{
    private readonly ConcurrentDictionary<string, OutboxMessage> _messages = new();

    /// <inheritdoc />
    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(message.MessageId);

        if (!_messages.TryAdd(message.MessageId, message))
        {
            throw new InvalidOperationException($"Message with ID '{message.MessageId}' already exists in outbox");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var pending = _messages.Values
            .Where(m => m.SentAt == null && m.RetryCount < m.MaxRetries)
            .Where(m => m.NextRetryAt == null || m.NextRetryAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    /// <inheritdoc />
    public Task MarkAsSentAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        if (_messages.TryGetValue(messageId, out var message))
        {
            message.SentAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkAsFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        if (_messages.TryGetValue(messageId, out var message))
        {
            message.RetryCount++;
            message.LastError = error;

            // Exponential backoff: 1s, 2s, 4s, 8s, 16s, etc.
            var backoffSeconds = Math.Pow(2, message.RetryCount);
            message.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CleanupSentMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - retentionPeriod;
        var toRemove = _messages.Values
            .Where(m => m.SentAt != null && m.SentAt < cutoffTime)
            .Select(m => m.MessageId)
            .ToList();

        foreach (var messageId in toRemove)
        {
            _messages.TryRemove(messageId, out _);
        }

        return Task.FromResult(toRemove.Count);
    }

    /// <summary>
    ///     Gets the total number of messages in the outbox (for testing).
    /// </summary>
    public int MessageCount => _messages.Count;

    /// <summary>
    ///     Clears all messages from the outbox (for testing).
    /// </summary>
    public void Clear() => _messages.Clear();
}
