using Quark.Abstractions;
using StackExchange.Redis;

namespace Quark.Messaging.Redis;

/// <summary>
///     Redis-based implementation of the inbox pattern.
///     Uses Redis sets to track processed message IDs with TTL support.
/// </summary>
public sealed class RedisInbox : IInbox
{
    private readonly IDatabase _database;
    private const string InboxKeyPrefix = "quark:inbox:";

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisInbox"/> class.
    /// </summary>
    /// <param name="database">The Redis database connection.</param>
    public RedisInbox(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetKey(actorId);
        return await _database.SetContainsAsync(key, messageId);
    }

    /// <inheritdoc />
    public async Task MarkAsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var key = GetKey(actorId);
        await _database.SetAddAsync(key, messageId);

        // Store timestamp in separate sorted set for cleanup
        var timestampKey = GetTimestampKey(actorId);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _database.SortedSetAddAsync(timestampKey, messageId, timestamp);
    }

    /// <inheritdoc />
    public async Task<int> CleanupOldEntriesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - retentionPeriod;
        var cutoffScore = cutoffTime.ToUnixTimeMilliseconds();
        var pattern = $"{InboxKeyPrefix}*:timestamps";
        var totalRemoved = 0;

        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
        await foreach (var timestampKey in server.KeysAsync(pattern: pattern))
        {
            // Get old message IDs
            var oldMessageIds = await _database.SortedSetRangeByScoreAsync(
                timestampKey,
                start: 0,
                stop: cutoffScore);

            if (oldMessageIds.Length > 0)
            {
                // Extract actor ID from key
                var actorId = ExtractActorIdFromTimestampKey(timestampKey.ToString());
                var setKey = GetKey(actorId);

                // Remove from both sets
                await _database.SetRemoveAsync(setKey, oldMessageIds);
                await _database.SortedSetRemoveAsync(timestampKey, oldMessageIds);

                totalRemoved += oldMessageIds.Length;
            }
        }

        return totalRemoved;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetProcessedAtAsync(
        string actorId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        var timestampKey = GetTimestampKey(actorId);
        var score = await _database.SortedSetScoreAsync(timestampKey, messageId);

        if (score.HasValue)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)score.Value);
        }

        return null;
    }

    private static string GetKey(string actorId) => $"{InboxKeyPrefix}{actorId}";
    private static string GetTimestampKey(string actorId) => $"{InboxKeyPrefix}{actorId}:timestamps";
    private static string ExtractActorIdFromTimestampKey(string key)
    {
        // Remove prefix and suffix
        var actorId = key.Substring(InboxKeyPrefix.Length);
        return actorId.Substring(0, actorId.Length - ":timestamps".Length);
    }
}
