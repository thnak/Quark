using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Messaging;

/// <summary>
///     In-memory implementation of the inbox pattern for development and testing.
///     This implementation is NOT suitable for production use as it does not persist
///     processed message IDs across restarts.
/// </summary>
public sealed class InMemoryInbox : IInbox
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedMessages = new();

    /// <inheritdoc />
    public Task<bool> IsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetKey(actorId, messageId);
        return Task.FromResult(_processedMessages.ContainsKey(key));
    }

    /// <inheritdoc />
    public Task MarkAsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetKey(actorId, messageId);
        _processedMessages.TryAdd(key, DateTimeOffset.UtcNow);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CleanupOldEntriesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - retentionPeriod;
        var toRemove = _processedMessages
            .Where(kvp => kvp.Value < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _processedMessages.TryRemove(key, out _);
        }

        return Task.FromResult(toRemove.Count);
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetProcessedAtAsync(
        string actorId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetKey(actorId, messageId);
        return Task.FromResult(_processedMessages.TryGetValue(key, out var timestamp)
            ? (DateTimeOffset?)timestamp
            : null);
    }

    /// <summary>
    ///     Gets the total number of processed message IDs (for testing).
    /// </summary>
    public int ProcessedCount => _processedMessages.Count;

    /// <summary>
    ///     Clears all processed message IDs (for testing).
    /// </summary>
    public void Clear() => _processedMessages.Clear();

    private static string GetKey(string actorId, string messageId) => $"{actorId}:{messageId}";
}
