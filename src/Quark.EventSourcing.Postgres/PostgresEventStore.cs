using System.Text.Json;
using Npgsql;
using Quark.EventSourcing;

namespace Quark.EventSourcing.Postgres;

/// <summary>
///     PostgreSQL-based event store implementation.
///     Uses a table with actor_id, sequence_number, and event_data columns.
/// </summary>
public sealed class PostgresEventStore : IEventStore
{
    private readonly string _connectionString;
    private readonly string _eventsTable;
    private readonly string _snapshotsTable;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgresEventStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="eventsTable">The table name for events (default: "quark_events").</param>
    /// <param name="snapshotsTable">The table name for snapshots (default: "quark_snapshots").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public PostgresEventStore(
        string connectionString,
        string eventsTable = "quark_events",
        string snapshotsTable = "quark_snapshots",
        JsonSerializerOptions? jsonOptions = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _eventsTable = eventsTable;
        _snapshotsTable = snapshotsTable;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    ///     Initializes the database schema. Call this once during application startup.
    /// </summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var createTablesSql = $@"
            CREATE TABLE IF NOT EXISTS {_eventsTable} (
                actor_id VARCHAR(255) NOT NULL,
                sequence_number BIGINT NOT NULL,
                event_type VARCHAR(255) NOT NULL,
                event_data JSONB NOT NULL,
                timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
                PRIMARY KEY (actor_id, sequence_number)
            );
            CREATE INDEX IF NOT EXISTS idx_{_eventsTable}_actor_id ON {_eventsTable}(actor_id);
            CREATE INDEX IF NOT EXISTS idx_{_eventsTable}_timestamp ON {_eventsTable}(timestamp);

            CREATE TABLE IF NOT EXISTS {_snapshotsTable} (
                actor_id VARCHAR(255) PRIMARY KEY,
                version BIGINT NOT NULL,
                snapshot_data JSONB NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );
        ";

        await using var command = new NpgsqlCommand(createTablesSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Check expected version for optimistic concurrency
            if (expectedVersion.HasValue)
            {
                var currentVersion = await GetCurrentVersionInternalAsync(connection, actorId, cancellationToken);
                if (currentVersion != expectedVersion.Value)
                {
                    throw new EventStoreConcurrencyException(expectedVersion.Value, currentVersion);
                }
            }

            long newVersion = expectedVersion ?? 0;
            foreach (var @event in events)
            {
                newVersion++;
                @event.ActorId = actorId;
                @event.SequenceNumber = newVersion;

                var eventJson = JsonSerializer.Serialize(@event, _jsonOptions);
                var sql = $@"
                    INSERT INTO {_eventsTable} (actor_id, sequence_number, event_type, event_data, timestamp)
                    VALUES (@actorId, @sequenceNumber, @eventType, @eventData::jsonb, @timestamp)";

                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@actorId", actorId);
                command.Parameters.AddWithValue("@sequenceNumber", newVersion);
                command.Parameters.AddWithValue("@eventType", @event.EventType);
                command.Parameters.AddWithValue("@eventData", eventJson);
                command.Parameters.AddWithValue("@timestamp", @event.Timestamp);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return newVersion;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainEvent>> ReadEventsAsync(
        string actorId,
        long fromVersion = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            SELECT event_data
            FROM {_eventsTable}
            WHERE actor_id = @actorId AND sequence_number >= @fromVersion
            ORDER BY sequence_number";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@fromVersion", fromVersion);

        var events = new List<DomainEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var eventJson = reader.GetString(0);
            var @event = JsonSerializer.Deserialize<DomainEvent>(eventJson, _jsonOptions);
            if (@event != null)
            {
                events.Add(@event);
            }
        }

        return events;
    }

    /// <inheritdoc />
    public async Task<long> GetCurrentVersionAsync(string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await GetCurrentVersionInternalAsync(connection, actorId, cancellationToken);
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var snapshotJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
        var sql = $@"
            INSERT INTO {_snapshotsTable} (actor_id, version, snapshot_data, created_at)
            VALUES (@actorId, @version, @snapshotData::jsonb, NOW())
            ON CONFLICT (actor_id) 
            DO UPDATE SET version = @version, snapshot_data = @snapshotData::jsonb, created_at = NOW()";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@snapshotData", snapshotJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(object Snapshot, long Version)?> LoadSnapshotAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            SELECT version, snapshot_data
            FROM {_snapshotsTable}
            WHERE actor_id = @actorId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var version = reader.GetInt64(0);
            var snapshotJson = reader.GetString(1);

            using var document = JsonDocument.Parse(snapshotJson);
            return (document.RootElement.Clone(), version);
        }

        return null;
    }

    private async Task<long> GetCurrentVersionInternalAsync(
        NpgsqlConnection connection,
        string actorId,
        CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT COALESCE(MAX(sequence_number), 0)
            FROM {_eventsTable}
            WHERE actor_id = @actorId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null ? Convert.ToInt64(result) : 0;
    }
}
