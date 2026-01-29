using System.Text.Json;
using Npgsql;
using Quark.Abstractions.Persistence;

namespace Quark.Storage.Postgres;

/// <summary>
///     PostgreSQL-based implementation of state storage with optimistic concurrency control.
///     Uses a table with actor_id, state_name, state_data, and version columns.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class PostgresStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgresStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing state (default: "quark_state").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public PostgresStateStorage(string connectionString, string tableName = "quark_state", JsonSerializerOptions? jsonOptions = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    ///     Initializes the database schema. Call this once during application startup.
    /// </summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {_tableName} (
                actor_id VARCHAR(255) NOT NULL,
                state_name VARCHAR(255) NOT NULL,
                state_data JSONB NOT NULL,
                version BIGINT NOT NULL DEFAULT 1,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                PRIMARY KEY (actor_id, state_name)
            );
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_updated_at ON {_tableName}(updated_at);
        ";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT state_data FROM {_tableName} WHERE actor_id = @actorId AND state_name = @stateName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@stateName", stateName);

        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json != null ? JsonSerializer.Deserialize<TState>(json, _jsonOptions) : null;
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT state_data, version FROM {_tableName} WHERE actor_id = @actorId AND state_name = @stateName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@stateName", stateName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
            var version = reader.GetInt64(1);
            var state = JsonSerializer.Deserialize<TState>(json, _jsonOptions);
            return state != null ? new StateWithVersion<TState>(state, version) : null;
        }

        return null;
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var sql = $@"
            INSERT INTO {_tableName} (actor_id, state_name, state_data, version, updated_at)
            VALUES (@actorId, @stateName, @stateData::jsonb, 1, NOW())
            ON CONFLICT (actor_id, state_name)
            DO UPDATE SET state_data = @stateData::jsonb, version = {_tableName}.version + 1, updated_at = NOW()
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@stateName", stateName);
        command.Parameters.AddWithValue("@stateData", json);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var json = JsonSerializer.Serialize(state, _jsonOptions);

        if (expectedVersion == null)
        {
            // First save - insert with version 1
            var sql = $@"
                INSERT INTO {_tableName} (actor_id, state_name, state_data, version, updated_at)
                VALUES (@actorId, @stateName, @stateData::jsonb, 1, NOW())
                ON CONFLICT (actor_id, state_name)
                DO NOTHING
                RETURNING version
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@actorId", actorId);
            command.Parameters.AddWithValue("@stateName", stateName);
            command.Parameters.AddWithValue("@stateData", json);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result == null)
            {
                // Conflict - state already exists, read actual version
                var actualVersion = await GetCurrentVersionAsync(connection, actorId, stateName, cancellationToken);
                throw new ConcurrencyException(0, actualVersion);
            }
            return (long)result;
        }
        else
        {
            // Update with version check
            var sql = $@"
                UPDATE {_tableName}
                SET state_data = @stateData::jsonb, version = version + 1, updated_at = NOW()
                WHERE actor_id = @actorId AND state_name = @stateName AND version = @expectedVersion
                RETURNING version
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@actorId", actorId);
            command.Parameters.AddWithValue("@stateName", stateName);
            command.Parameters.AddWithValue("@stateData", json);
            command.Parameters.AddWithValue("@expectedVersion", expectedVersion.Value);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result == null)
            {
                // Version mismatch or not found
                var actualVersion = await GetCurrentVersionAsync(connection, actorId, stateName, cancellationToken);
                throw new ConcurrencyException(expectedVersion.Value, actualVersion);
            }
            return (long)result;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE actor_id = @actorId AND state_name = @stateName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@stateName", stateName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> GetCurrentVersionAsync(NpgsqlConnection connection, string actorId, string stateName, CancellationToken cancellationToken)
    {
        var sql = $"SELECT version FROM {_tableName} WHERE actor_id = @actorId AND state_name = @stateName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@stateName", stateName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null ? (long)result : 0L;
    }
}
