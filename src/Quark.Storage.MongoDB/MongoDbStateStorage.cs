using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Quark.Abstractions.Persistence;

namespace Quark.Storage.MongoDB;

/// <summary>
///     MongoDB-based implementation of state storage with optimistic concurrency control.
///     Uses BSON serialization for efficient storage of complex state objects.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class MongoDbStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly IMongoCollection<StateDocument> _collection;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MongoDbStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="database">The MongoDB database instance.</param>
    /// <param name="collectionName">The collection name for storing state (default: "quark_state").</param>
    public MongoDbStateStorage(IMongoDatabase database, string collectionName = "quark_state")
    {
        if (database == null)
            throw new ArgumentNullException(nameof(database));

        _collection = database.GetCollection<StateDocument>(collectionName);
    }

    /// <summary>
    ///     Initializes indexes for the collection. Call this once during application startup.
    /// </summary>
    public async Task InitializeIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexKeys = Builders<StateDocument>.IndexKeys
            .Ascending(d => d.ActorId)
            .Ascending(d => d.StateName);

        var indexModel = new CreateIndexModel<StateDocument>(
            indexKeys,
            new CreateIndexOptions { Unique = true });

        await _collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancellationToken);

        // Index for time-based queries
        var timeIndexKeys = Builders<StateDocument>.IndexKeys.Descending(d => d.UpdatedAt);
        var timeIndexModel = new CreateIndexModel<StateDocument>(timeIndexKeys);
        await _collection.Indexes.CreateOneAsync(timeIndexModel, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var filter = Builders<StateDocument>.Filter.And(
            Builders<StateDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<StateDocument>.Filter.Eq(d => d.StateName, stateName));

        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document?.GetState<TState>();
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var filter = Builders<StateDocument>.Filter.And(
            Builders<StateDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<StateDocument>.Filter.Eq(d => d.StateName, stateName));

        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (document == null)
            return null;

        var state = document.GetState<TState>();
        return state != null ? new StateWithVersion<TState>(state, document.Version) : null;
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var filter = Builders<StateDocument>.Filter.And(
            Builders<StateDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<StateDocument>.Filter.Eq(d => d.StateName, stateName));

        var update = Builders<StateDocument>.Update
            .Set(d => d.StateData, state.ToBsonDocument())
            .Inc(d => d.Version, 1)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        var options = new FindOneAndUpdateOptions<StateDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        var document = new StateDocument
        {
            ActorId = actorId,
            StateName = stateName,
            StateData = state.ToBsonDocument(),
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        if (expectedVersion == null)
        {
            // First save - insert with version 1
            try
            {
                await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
                return 1L;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Conflict - state already exists
                var existing = await LoadWithVersionAsync(actorId, stateName, cancellationToken);
                var actualVersion = existing?.Version ?? 0L;
                throw new ConcurrencyException(0, actualVersion);
            }
        }
        else
        {
            // Update with version check
            var filter = Builders<StateDocument>.Filter.And(
                Builders<StateDocument>.Filter.Eq(d => d.ActorId, actorId),
                Builders<StateDocument>.Filter.Eq(d => d.StateName, stateName),
                Builders<StateDocument>.Filter.Eq(d => d.Version, expectedVersion.Value));

            var newVersion = expectedVersion.Value + 1;
            var update = Builders<StateDocument>.Update
                .Set(d => d.StateData, state.ToBsonDocument())
                .Set(d => d.Version, newVersion)
                .Set(d => d.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                // Version mismatch or not found
                var existing = await LoadWithVersionAsync(actorId, stateName, cancellationToken);
                var actualVersion = existing?.Version ?? 0L;
                throw new ConcurrencyException(expectedVersion.Value, actualVersion);
            }

            return newVersion;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var filter = Builders<StateDocument>.Filter.And(
            Builders<StateDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<StateDocument>.Filter.Eq(d => d.StateName, stateName));

        await _collection.DeleteOneAsync(filter, cancellationToken);
    }

    /// <summary>
    ///     Internal document structure for storing state in MongoDB.
    /// </summary>
    private class StateDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("actor_id")]
        public required string ActorId { get; set; }

        [BsonElement("state_name")]
        public required string StateName { get; set; }

        [BsonElement("state_data")]
        public required BsonDocument StateData { get; set; }

        [BsonElement("version")]
        public long Version { get; set; }

        [BsonElement("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public TState? GetState<TState>() where TState : class
        {
            return BsonSerializer.Deserialize<TState>(StateData);
        }
    }
}
