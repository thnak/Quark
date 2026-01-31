using System.Text.Json;
using Cassandra;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Storage.Cassandra;

/// <summary>
///     Cassandra-based implementation of persistent reminder storage.
///     Optimized for time-series queries with TimeWindowCompactionStrategy.
/// </summary>
public sealed class CassandraReminderTable : IReminderTable
{
    private readonly ISession _session;
    private readonly string _keyspace;
    private readonly string _tableName;
    private readonly IConsistentHashRing? _hashRing;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConsistencyLevel _readConsistency;
    private readonly ConsistencyLevel _writeConsistency;

    private PreparedStatement? _getRemindersStatement;
    private PreparedStatement? _deleteReminderStatement;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CassandraReminderTable"/> class.
    /// </summary>
    /// <param name="session">The Cassandra session instance.</param>
    /// <param name="keyspace">The keyspace name (default: "quark").</param>
    /// <param name="tableName">The table name for storing reminders (default: "reminders").</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    /// <param name="readConsistency">Read consistency level (default: LOCAL_QUORUM).</param>
    /// <param name="writeConsistency">Write consistency level (default: LOCAL_QUORUM).</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public CassandraReminderTable(
        ISession session,
        string keyspace = "quark",
        string tableName = "reminders",
        IConsistentHashRing? hashRing = null,
        ConsistencyLevel readConsistency = ConsistencyLevel.LocalQuorum,
        ConsistencyLevel writeConsistency = ConsistencyLevel.LocalQuorum,
        JsonSerializerOptions? jsonOptions = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _keyspace = keyspace;
        _tableName = tableName;
        _hashRing = hashRing;
        _readConsistency = readConsistency;
        _writeConsistency = writeConsistency;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    ///     Initializes the database schema. Call this once during application startup.
    /// </summary>
    /// <param name="replicationStrategy">Replication strategy (default: SimpleStrategy with replication_factor=3).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeSchemaAsync(
        string replicationStrategy = "{'class': 'SimpleStrategy', 'replication_factor': 3}",
        CancellationToken cancellationToken = default)
    {
        // Create keyspace if not exists
        var createKeyspace = $@"
            CREATE KEYSPACE IF NOT EXISTS {_keyspace}
            WITH replication = {replicationStrategy}
        ";
        await _session.ExecuteAsync(new SimpleStatement(createKeyspace));

        // Create table optimized for time-series queries
        var createTable = $@"
            CREATE TABLE IF NOT EXISTS {_keyspace}.{_tableName} (
                actor_id text,
                name text,
                actor_type text,
                due_time timestamp,
                period_ticks bigint,
                next_fire_time timestamp,
                last_fired_at timestamp,
                reminder_data text,
                PRIMARY KEY (actor_id, name)
            ) WITH compaction = {{'class': 'TimeWindowCompactionStrategy'}}
        ";
        await _session.ExecuteAsync(new SimpleStatement(createTable));

        // Create materialized view for time-based queries (due reminders)
        var createView = $@"
            CREATE MATERIALIZED VIEW IF NOT EXISTS {_keyspace}.{_tableName}_by_time AS
            SELECT * FROM {_keyspace}.{_tableName}
            WHERE next_fire_time IS NOT NULL AND actor_id IS NOT NULL AND name IS NOT NULL
            PRIMARY KEY (next_fire_time, actor_id, name)
            WITH CLUSTERING ORDER BY (actor_id ASC, name ASC)
        ";
        await _session.ExecuteAsync(new SimpleStatement(createView));

        // Prepare statements
        _getRemindersStatement = await _session.PrepareAsync($@"
            SELECT reminder_data
            FROM {_keyspace}.{_tableName}
            WHERE actor_id = ?
        ");
        _getRemindersStatement.SetConsistencyLevel(_readConsistency);

        _deleteReminderStatement = await _session.PrepareAsync($@"
            DELETE FROM {_keyspace}.{_tableName}
            WHERE actor_id = ? AND name = ?
        ");
        _deleteReminderStatement.SetConsistencyLevel(_writeConsistency);
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(reminder, _jsonOptions);

        var cql = $@"
            INSERT INTO {_keyspace}.{_tableName} (actor_id, name, actor_type, due_time, period_ticks, next_fire_time, last_fired_at, reminder_data)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        ";

        var statement = new SimpleStatement(
            cql,
            reminder.ActorId,
            reminder.Name,
            reminder.ActorType,
            reminder.DueTime.UtcDateTime,
            reminder.Period?.Ticks,
            reminder.NextFireTime.UtcDateTime,
            reminder.LastFiredAt?.UtcDateTime,
            json);

        statement.SetConsistencyLevel(_writeConsistency);

        await _session.ExecuteAsync(statement);
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        if (_deleteReminderStatement == null)
            throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

        var bound = _deleteReminderStatement.Bind(actorId, name);
        await _session.ExecuteAsync(bound);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (_getRemindersStatement == null)
            throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

        var bound = _getRemindersStatement.Bind(actorId);
        var rowSet = await _session.ExecuteAsync(bound);

        var reminders = new List<Reminder>();
        foreach (var row in rowSet)
        {
            var json = row.GetValue<string>("reminder_data");
            var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
            if (reminder != null)
            {
                reminders.Add(reminder);
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
        // Query the materialized view for time-based lookup
        var cql = $@"
            SELECT reminder_data
            FROM {_keyspace}.{_tableName}_by_time
            WHERE next_fire_time <= ?
            ALLOW FILTERING
        ";

        var statement = new SimpleStatement(cql, utcNow.UtcDateTime);
        statement.SetConsistencyLevel(_readConsistency);

        var rowSet = await _session.ExecuteAsync(statement);

        var reminders = new List<Reminder>();
        foreach (var row in rowSet)
        {
            var json = row.GetValue<string>("reminder_data");
            var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
            if (reminder != null && IsReminderOwnedBySilo(reminder, siloId))
            {
                reminders.Add(reminder);
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
        // First, get the current reminder to preserve other fields
        var selectCql = $@"
            SELECT reminder_data
            FROM {_keyspace}.{_tableName}
            WHERE actor_id = ? AND name = ?
        ";

        var selectStatement = new SimpleStatement(selectCql, actorId, name);
        selectStatement.SetConsistencyLevel(_readConsistency);

        var rowSet = await _session.ExecuteAsync(selectStatement);
        var row = rowSet.FirstOrDefault();

        if (row == null)
            return;

        var json = row.GetValue<string>("reminder_data");
        var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);

        if (reminder != null)
        {
            reminder.LastFiredAt = lastFiredAt;
            reminder.NextFireTime = nextFireTime;

            // Update the reminder
            await RegisterAsync(reminder, cancellationToken);
        }
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
    }
}
