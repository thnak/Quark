using System.Text.Json;
using Quark.Abstractions;
using StackExchange.Redis;

namespace Quark.Messaging.Redis;

/// <summary>
///     Redis-based implementation of the outbox pattern.
///     Uses Redis sorted sets for efficient message ordering and retrieval.
/// </summary>
public sealed class RedisOutbox : IOutbox
{
    private readonly IDatabase _database;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string OutboxKeyPrefix = "quark:outbox:";
    private const string OutboxSortedSetKey = "quark:outbox:pending";

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisOutbox"/> class.
    /// </summary>
    /// <param name="database">The Redis database connection.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public RedisOutbox(IDatabase database, JsonSerializerOptions? jsonOptions = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(message.MessageId);

        var key = GetMessageKey(message.MessageId);
        var json = JsonSerializer.Serialize(message, _jsonOptions);

        // Store message data
        await _database.StringSetAsync(key, json);

        // Add to sorted set with creation timestamp as score for ordering
        var score = message.CreatedAt.ToUnixTimeMilliseconds();
        await _database.SortedSetAddAsync(OutboxSortedSetKey, message.MessageId, score);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Get message IDs from sorted set (ordered by creation time)
        var messageIds = await _database.SortedSetRangeByScoreAsync(
            OutboxSortedSetKey,
            start: 0,
            stop: now.ToUnixTimeMilliseconds(),
            take: batchSize);

        if (messageIds.Length == 0)
            return Array.Empty<OutboxMessage>();

        // Retrieve message details
        var messages = new List<OutboxMessage>();
        foreach (var messageId in messageIds)
        {
            var key = GetMessageKey(messageId.ToString());
            var json = await _database.StringGetAsync(key);

            if (json.HasValue)
            {
                var message = JsonSerializer.Deserialize<OutboxMessage>(json.ToString(), _jsonOptions);
                if (message != null &&
                    message.SentAt == null &&
                    message.RetryCount < message.MaxRetries &&
                    (message.NextRetryAt == null || message.NextRetryAt <= now))
                {
                    messages.Add(message);
                }
            }
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task MarkAsSentAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetMessageKey(messageId);
        var json = await _database.StringGetAsync(key);

        if (json.HasValue)
        {
            var message = JsonSerializer.Deserialize<OutboxMessage>(json.ToString(), _jsonOptions);
            if (message != null)
            {
                message.SentAt = DateTimeOffset.UtcNow;
                var updatedJson = JsonSerializer.Serialize(message, _jsonOptions);
                await _database.StringSetAsync(key, updatedJson);

                // Remove from pending sorted set
                await _database.SortedSetRemoveAsync(OutboxSortedSetKey, messageId);
            }
        }
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetMessageKey(messageId);
        var json = await _database.StringGetAsync(key);

        if (json.HasValue)
        {
            var message = JsonSerializer.Deserialize<OutboxMessage>(json.ToString(), _jsonOptions);
            if (message != null)
            {
                message.RetryCount++;
                message.LastError = error;

                // Exponential backoff
                var backoffSeconds = Math.Pow(2, message.RetryCount);
                message.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);

                var updatedJson = JsonSerializer.Serialize(message, _jsonOptions);
                await _database.StringSetAsync(key, updatedJson);

                // Update score in sorted set to next retry time
                if (message.RetryCount < message.MaxRetries)
                {
                    var newScore = message.NextRetryAt.Value.ToUnixTimeMilliseconds();
                    await _database.SortedSetAddAsync(OutboxSortedSetKey, messageId, newScore);
                }
                else
                {
                    // Max retries exceeded, remove from pending
                    await _database.SortedSetRemoveAsync(OutboxSortedSetKey, messageId);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupSentMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - retentionPeriod;
        var pattern = $"{OutboxKeyPrefix}*";
        var count = 0;

        await foreach (var key in GetKeysAsync(pattern))
        {
            var json = await _database.StringGetAsync(key);
            if (json.HasValue)
            {
                var message = JsonSerializer.Deserialize<OutboxMessage>(json.ToString(), _jsonOptions);
                if (message?.SentAt != null && message.SentAt < cutoffTime)
                {
                    await _database.KeyDeleteAsync(key);
                    count++;
                }
            }
        }

        return count;
    }

    private static string GetMessageKey(string messageId) => $"{OutboxKeyPrefix}{messageId}";

    private async IAsyncEnumerable<RedisKey> GetKeysAsync(string pattern)
    {
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            yield return key;
        }
    }
}
