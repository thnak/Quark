using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Storage.MongoDB;

/// <summary>
///     MongoDB-based implementation of persistent reminder storage.
///     Uses indexes for efficient time-based and actor-based queries.
/// </summary>
public sealed class MongoDbReminderTable : IReminderTable
{
    private readonly IMongoCollection<ReminderDocument> _collection;
    private readonly IConsistentHashRing? _hashRing;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MongoDbReminderTable"/> class.
    /// </summary>
    /// <param name="database">The MongoDB database instance.</param>
    /// <param name="collectionName">The collection name for storing reminders (default: "quark_reminders").</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    public MongoDbReminderTable(
        IMongoDatabase database,
        string collectionName = "quark_reminders",
        IConsistentHashRing? hashRing = null)
    {
        if (database == null)
            throw new ArgumentNullException(nameof(database));

        _collection = database.GetCollection<ReminderDocument>(collectionName);
        _hashRing = hashRing;
    }

    /// <summary>
    ///     Initializes indexes for the collection. Call this once during application startup.
    /// </summary>
    public async Task InitializeIndexesAsync(CancellationToken cancellationToken = default)
    {
        var actorIndexKeys = Builders<ReminderDocument>.IndexKeys
            .Ascending(d => d.ActorId)
            .Ascending(d => d.Name);

        var actorIndexModel = new CreateIndexModel<ReminderDocument>(
            actorIndexKeys,
            new CreateIndexOptions { Unique = true });

        await _collection.Indexes.CreateOneAsync(actorIndexModel, cancellationToken: cancellationToken);

        // Index for time-based queries (finding due reminders)
        var timeIndexKeys = Builders<ReminderDocument>.IndexKeys.Ascending(d => d.NextFireTime);
        var timeIndexModel = new CreateIndexModel<ReminderDocument>(timeIndexKeys);
        await _collection.Indexes.CreateOneAsync(timeIndexModel, cancellationToken: cancellationToken);

        // Index for actor type queries
        var typeIndexKeys = Builders<ReminderDocument>.IndexKeys.Ascending(d => d.ActorType);
        var typeIndexModel = new CreateIndexModel<ReminderDocument>(typeIndexKeys);
        await _collection.Indexes.CreateOneAsync(typeIndexModel, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var document = ReminderDocument.FromReminder(reminder);

        var filter = Builders<ReminderDocument>.Filter.And(
            Builders<ReminderDocument>.Filter.Eq(d => d.ActorId, reminder.ActorId),
            Builders<ReminderDocument>.Filter.Eq(d => d.Name, reminder.Name));

        var options = new ReplaceOptions { IsUpsert = true };

        await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ReminderDocument>.Filter.And(
            Builders<ReminderDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<ReminderDocument>.Filter.Eq(d => d.Name, name));

        await _collection.DeleteOneAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ReminderDocument>.Filter.Eq(d => d.ActorId, actorId);

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);

        return documents.Select(d => d.ToReminder()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetDueRemindersForSiloAsync(
        string siloId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ReminderDocument>.Filter.Lte(d => d.NextFireTime, utcNow);

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);

        var reminders = documents
            .Select(d => d.ToReminder())
            .Where(r => IsReminderOwnedBySilo(r, siloId))
            .ToList();

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
        var filter = Builders<ReminderDocument>.Filter.And(
            Builders<ReminderDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<ReminderDocument>.Filter.Eq(d => d.Name, name));

        var update = Builders<ReminderDocument>.Update
            .Set(d => d.LastFiredAt, lastFiredAt)
            .Set(d => d.NextFireTime, nextFireTime);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
    }

    /// <summary>
    ///     Internal document structure for storing reminders in MongoDB.
    /// </summary>
    private class ReminderDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("actor_id")]
        public required string ActorId { get; set; }

        [BsonElement("name")]
        public required string Name { get; set; }

        [BsonElement("actor_type")]
        public required string ActorType { get; set; }

        [BsonElement("due_time")]
        public DateTimeOffset DueTime { get; set; }

        [BsonElement("period_ticks")]
        public long? PeriodTicks { get; set; }

        [BsonElement("next_fire_time")]
        public DateTimeOffset NextFireTime { get; set; }

        [BsonElement("last_fired_at")]
        public DateTimeOffset? LastFiredAt { get; set; }

        public static ReminderDocument FromReminder(Reminder reminder)
        {
            return new ReminderDocument
            {
                ActorId = reminder.ActorId,
                Name = reminder.Name,
                ActorType = reminder.ActorType,
                DueTime = reminder.DueTime,
                PeriodTicks = reminder.Period?.Ticks,
                NextFireTime = reminder.NextFireTime,
                LastFiredAt = reminder.LastFiredAt
            };
        }

        public Reminder ToReminder()
        {
            return new Reminder(
                actorId: ActorId,
                name: Name,
                actorType: ActorType,
                dueTime: DueTime,
                period: PeriodTicks.HasValue ? TimeSpan.FromTicks(PeriodTicks.Value) : null)
            {
                NextFireTime = NextFireTime,
                LastFiredAt = LastFiredAt
            };
        }
    }
}
