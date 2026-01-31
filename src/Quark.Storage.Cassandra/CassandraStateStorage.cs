using System.Text.Json;
using Cassandra;
using Quark.Abstractions.Persistence;

namespace Quark.Storage.Cassandra;

/// <summary>
///     Cassandra-based implementation of state storage with optimistic concurrency control.
///     Supports tunable consistency levels and multi-datacenter replication.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class CassandraStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly ISession _session;
    private readonly string _keyspace;
    private readonly string _tableName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConsistencyLevel _readConsistency;
    private readonly ConsistencyLevel _writeConsistency;

    private PreparedStatement? _loadStatement;
    private PreparedStatement? _saveStatement;
    private PreparedStatement? _deleteStatement;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CassandraStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="session">The Cassandra session instance.</param>
    /// <param name="keyspace">The keyspace name (default: "quark").</param>
    /// <param name="tableName">The table name for storing state (default: "state").</param>
    /// <param name="readConsistency">Read consistency level (default: LOCAL_QUORUM).</param>
    /// <param name="writeConsistency">Write consistency level (default: LOCAL_QUORUM).</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public CassandraStateStorage(
        ISession session,
        string keyspace = "quark",
        string tableName = "state",
        ConsistencyLevel readConsistency = ConsistencyLevel.LocalQuorum,
        ConsistencyLevel writeConsistency = ConsistencyLevel.LocalQuorum,
        JsonSerializerOptions? jsonOptions = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _keyspace = keyspace;
        _tableName = tableName;
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

        // Create table with optimistic locking
        var createTable = $@"
            CREATE TABLE IF NOT EXISTS {_keyspace}.{_tableName} (
                actor_id text,
                state_name text,
                state_data text,
                version bigint,
                updated_at timestamp,
                PRIMARY KEY (actor_id, state_name)
            ) WITH compaction = {{'class': 'TimeWindowCompactionStrategy'}}
              AND default_time_to_live = 0
        ";
        await _session.ExecuteAsync(new SimpleStatement(createTable));

        // Prepare statements for better performance
        _loadStatement = await _session.PrepareAsync($@"
            SELECT state_data, version
            FROM {_keyspace}.{_tableName}
            WHERE actor_id = ? AND state_name = ?
        ");
        _loadStatement.SetConsistencyLevel(_readConsistency);

        _saveStatement = await _session.PrepareAsync($@"
            INSERT INTO {_keyspace}.{_tableName} (actor_id, state_name, state_data, version, updated_at)
            VALUES (?, ?, ?, ?, toTimestamp(now()))
            IF NOT EXISTS
        ");
        _saveStatement.SetConsistencyLevel(_writeConsistency);

        _deleteStatement = await _session.PrepareAsync($@"
            DELETE FROM {_keyspace}.{_tableName}
            WHERE actor_id = ? AND state_name = ?
        ");
        _deleteStatement.SetConsistencyLevel(_writeConsistency);
    }

    /// <inheritdoc />
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        if (_loadStatement == null)
            throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

        var bound = _loadStatement.Bind(actorId, stateName);
        var rowSet = await _session.ExecuteAsync(bound);
        var row = rowSet.FirstOrDefault();

        if (row == null)
            return null;

        var json = row.GetValue<string>("state_data");
        return JsonSerializer.Deserialize<TState>(json, _jsonOptions);
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        if (_loadStatement == null)
            throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

        var bound = _loadStatement.Bind(actorId, stateName);
        var rowSet = await _session.ExecuteAsync(bound);
        var row = rowSet.FirstOrDefault();

        if (row == null)
            return null;

        var json = row.GetValue<string>("state_data");
        var version = row.GetValue<long>("version");
        var state = JsonSerializer.Deserialize<TState>(json, _jsonOptions);

        return state != null ? new StateWithVersion<TState>(state, version) : null;
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);

        // Use lightweight transaction for upsert with version increment
        var cql = $@"
            UPDATE {_keyspace}.{_tableName}
            SET state_data = ?, version = version + 1, updated_at = toTimestamp(now())
            WHERE actor_id = ? AND state_name = ?
        ";

        var statement = new SimpleStatement(cql, json, actorId, stateName);
        statement.SetConsistencyLevel(_writeConsistency);

        await _session.ExecuteAsync(statement);
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);

        if (expectedVersion == null)
        {
            // First save - use conditional insert
            if (_saveStatement == null)
                throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

            var bound = _saveStatement.Bind(actorId, stateName, json, 1L);
            var rowSet = await _session.ExecuteAsync(bound);
            var row = rowSet.FirstOrDefault();

            if (row != null && !row.GetValue<bool>("[applied]"))
            {
                // Conflict - state already exists
                var existing = await LoadWithVersionAsync(actorId, stateName, cancellationToken);
                var actualVersion = existing?.Version ?? 0L;
                throw new ConcurrencyException(0, actualVersion);
            }

            return 1L;
        }
        else
        {
            // Update with version check using lightweight transaction
            var newVersion = expectedVersion.Value + 1;
            var cql = $@"
                UPDATE {_keyspace}.{_tableName}
                SET state_data = ?, version = ?, updated_at = toTimestamp(now())
                WHERE actor_id = ? AND state_name = ?
                IF version = ?
            ";

            var statement = new SimpleStatement(cql, json, newVersion, actorId, stateName, expectedVersion.Value);
            statement.SetConsistencyLevel(_writeConsistency);

            var rowSet = await _session.ExecuteAsync(statement);
            var row = rowSet.FirstOrDefault();

            if (row == null || !row.GetValue<bool>("[applied]"))
            {
                // Version mismatch
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
        if (_deleteStatement == null)
            throw new InvalidOperationException("InitializeSchemaAsync must be called before using the storage.");

        var bound = _deleteStatement.Bind(actorId, stateName);
        await _session.ExecuteAsync(bound);
    }
}
