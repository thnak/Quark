using System.Text.Json;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;
using StackExchange.Redis;

namespace Quark.Storage.Redis;

/// <summary>
///     Redis-based implementation of persistent reminder storage.
///     Uses Redis sorted sets for efficient time-based queries.
/// </summary>
public sealed class RedisReminderTable : IReminderTable
{
    private readonly IDatabase _database;
    private readonly IConsistentHashRing? _hashRing;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisReminderTable"/> class.
    /// </summary>
    /// <param name="database">The Redis database instance.</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public RedisReminderTable(IDatabase database, IConsistentHashRing? hashRing = null, JsonSerializerOptions? jsonOptions = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _hashRing = hashRing;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var key = GetReminderKey(reminder.ActorId);
        var json = JsonSerializer.Serialize(reminder, _jsonOptions);
        var score = reminder.NextFireTime.ToUnixTimeMilliseconds();
        
        // Store reminder in sorted set (for time-based queries) and hash (for retrieval)
        var transaction = _database.CreateTransaction();
        _ = transaction.SortedSetAddAsync(GetAllRemindersKey(), reminder.GetId(), score);
        _ = transaction.HashSetAsync(key, reminder.Name, json);
        await transaction.ExecuteAsync();
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        var key = GetReminderKey(actorId);
        var reminderId = $"{actorId}:{name}";
        
        var transaction = _database.CreateTransaction();
        _ = transaction.HashDeleteAsync(key, name);
        _ = transaction.SortedSetRemoveAsync(GetAllRemindersKey(), reminderId);
        await transaction.ExecuteAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var key = GetReminderKey(actorId);
        var entries = await _database.HashGetAllAsync(key);
        
        var reminders = new List<Reminder>();
        foreach (var entry in entries)
        {
            if (!entry.Value.IsNullOrEmpty)
            {
                var reminder = JsonSerializer.Deserialize<Reminder>(entry.Value.ToString(), _jsonOptions);
                if (reminder != null)
                {
                    reminders.Add(reminder);
                }
            }
        }
        
        return reminders;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetDueRemindersForSiloAsync(
        string siloId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var maxScore = utcNow.ToUnixTimeMilliseconds();
        var dueReminderIds = await _database.SortedSetRangeByScoreAsync(
            GetAllRemindersKey(),
            double.NegativeInfinity,
            maxScore);

        var reminders = new List<Reminder>();
        
        foreach (var reminderId in dueReminderIds)
        {
            if (reminderId.IsNullOrEmpty)
                continue;

            var parts = reminderId.ToString().Split(':');
            if (parts.Length < 2)
                continue;

            var actorId = parts[0];
            var name = parts[1];
            
            var key = GetReminderKey(actorId);
            var json = await _database.HashGetAsync(key, name);
            
            if (!json.IsNullOrEmpty)
            {
                var reminder = JsonSerializer.Deserialize<Reminder>(json.ToString(), _jsonOptions);
                if (reminder != null && IsReminderOwnedBySilo(reminder, siloId))
                {
                    reminders.Add(reminder);
                }
            }
        }
        
        return reminders;
    }

    /// <inheritdoc />
    public async Task UpdateFireTimeAsync(
        string actorId,
        string name,
        DateTimeOffset lastFiredAt,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        var key = GetReminderKey(actorId);
        var json = await _database.HashGetAsync(key, name);
        
        if (json.IsNullOrEmpty)
            return;

        var reminder = JsonSerializer.Deserialize<Reminder>(json.ToString(), _jsonOptions);
        if (reminder != null)
        {
            reminder.LastFiredAt = lastFiredAt;
            reminder.NextFireTime = nextFireTime;
            
            var updatedJson = JsonSerializer.Serialize(reminder, _jsonOptions);
            var score = nextFireTime.ToUnixTimeMilliseconds();
            
            var transaction = _database.CreateTransaction();
            _ = transaction.HashSetAsync(key, name, updatedJson);
            _ = transaction.SortedSetAddAsync(GetAllRemindersKey(), reminder.GetId(), score);
            await transaction.ExecuteAsync();
        }
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
    }

    private static string GetReminderKey(string actorId)
    {
        return $"quark:reminders:{actorId}";
    }

    private static string GetAllRemindersKey()
    {
        return "quark:reminders:all";
    }
}
