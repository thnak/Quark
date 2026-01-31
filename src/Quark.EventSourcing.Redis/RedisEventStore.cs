using System.Text.Json;
using Quark.EventSourcing;
using StackExchange.Redis;

namespace Quark.EventSourcing.Redis;

/// <summary>
///     Redis Streams-based event store implementation.
///     Uses Redis Streams which are optimized for append-only event logs.
/// </summary>
public sealed class RedisEventStore : IEventStore
{
    private readonly IDatabase _database;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string StreamKeyPrefix = "quark:events:";
    private const string SnapshotKeyPrefix = "quark:snapshot:";

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisEventStore"/> class.
    /// </summary>
    /// <param name="database">The Redis database connection.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public RedisEventStore(IDatabase database, JsonSerializerOptions? jsonOptions = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task<long> AppendEventsAsync(
        string actorId,
        IReadOnlyList<DomainEvent> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
            return expectedVersion ?? 0;

        var streamKey = GetStreamKey(actorId);

        // Check expected version for optimistic concurrency
        if (expectedVersion.HasValue)
        {
            var currentVersion = await GetCurrentVersionAsync(actorId, cancellationToken);
            if (currentVersion != expectedVersion.Value)
            {
                throw new EventStoreConcurrencyException(expectedVersion.Value, currentVersion);
            }
        }

        // Append events to stream
        long newVersion = expectedVersion ?? 0;
        foreach (var @event in events)
        {
            newVersion++;
            @event.SequenceNumber = newVersion;

            var eventJson = JsonSerializer.Serialize(@event, _jsonOptions);
            var values = new NameValueEntry[]
            {
                new("eventType", @event.EventType),
                new("sequenceNumber", newVersion.ToString()),
                new("timestamp", @event.Timestamp.ToUnixTimeMilliseconds().ToString()),
                new("eventData", eventJson)
            };

            await _database.StreamAddAsync(streamKey, values);
        }

        return newVersion;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainEvent>> ReadEventsAsync(
        string actorId,
        long fromVersion = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        var streamKey = GetStreamKey(actorId);
        var events = new List<DomainEvent>();

        // Read from stream starting at the requested version
        var startId = fromVersion == 0 ? "-" : $"{fromVersion-1}-0";
        var streamEntries = await _database.StreamReadAsync(streamKey, startId);

        foreach (var entry in streamEntries)
        {
            var eventJson = entry.Values.FirstOrDefault(v => v.Name == "eventData").Value;
            if (!eventJson.IsNullOrEmpty)
            {
                var @event = JsonSerializer.Deserialize<DomainEvent>(eventJson.ToString(), _jsonOptions);
                if (@event != null)
                {
                    events.Add(@event);
                }
            }
        }

        return events.Where(e => e.SequenceNumber >= fromVersion).ToList();
    }

    /// <inheritdoc />
    public async Task<long> GetCurrentVersionAsync(string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        var streamKey = GetStreamKey(actorId);
        var length = await _database.StreamLengthAsync(streamKey);

        if (length == 0)
            return 0;

        // Get the last entry to read its sequence number
        var lastEntries = await _database.StreamRangeAsync(streamKey, "-", "+", count: 1, messageOrder: Order.Descending);
        if (lastEntries.Length == 0)
            return 0;

        var sequenceValue = lastEntries[0].Values.FirstOrDefault(v => v.Name == "sequenceNumber").Value;
        if (sequenceValue.IsNullOrEmpty || !long.TryParse(sequenceValue.ToString(), out var sequenceNumber))
            return length; // Fallback to stream length

        return sequenceNumber;
    }

    /// <inheritdoc />
    public async Task SaveSnapshotAsync(
        string actorId,
        object snapshot,
        long version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var snapshotKey = GetSnapshotKey(actorId);
        var snapshotData = new
        {
            Snapshot = snapshot,
            Version = version,
            Timestamp = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(snapshotData, _jsonOptions);
        await _database.StringSetAsync(snapshotKey, json);
    }

    /// <inheritdoc />
    public async Task<(object Snapshot, long Version)?> LoadSnapshotAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        var snapshotKey = GetSnapshotKey(actorId);
        var json = await _database.StringGetAsync(snapshotKey);

        if (json.IsNullOrEmpty)
            return null;

        using var document = JsonDocument.Parse(json.ToString());
        var root = document.RootElement;

        var version = root.GetProperty("Version").GetInt64();
        var snapshot = root.GetProperty("Snapshot");

        // Return the snapshot as a JsonElement (caller can deserialize to their type)
        return (snapshot, version);
    }

    private static string GetStreamKey(string actorId) => $"{StreamKeyPrefix}{actorId}";
    private static string GetSnapshotKey(string actorId) => $"{SnapshotKeyPrefix}{actorId}";
}
