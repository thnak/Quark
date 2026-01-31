using System.Text.Json;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using Quark.Abstractions.Persistence;

namespace Quark.Storage.SqlServer;

/// <summary>
///     SQL Server-based implementation of state storage with optimistic concurrency control.
///     Uses a table with actor_id, state_name, state_data, and version columns.
///     Includes connection pooling and retry policies for resilience.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class SqlServerStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AsyncRetryPolicy _retryPolicy;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="tableName">The table name for storing state (default: "QuarkState").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="retryPolicy">Optional custom retry policy. If null, uses default exponential backoff.</param>
    public SqlServerStateStorage(
        string connectionString, 
        string tableName = "QuarkState", 
        JsonSerializerOptions? jsonOptions = null,
        AsyncRetryPolicy? retryPolicy = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        _retryPolicy = retryPolicy ?? Policy
            .Handle<SqlException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    /// <summary>
    ///     Initializes the database schema. Call this once during application startup.
    /// </summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var createTableSql = $@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{_tableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    ActorId NVARCHAR(255) NOT NULL,
                    StateName NVARCHAR(255) NOT NULL,
                    StateData NVARCHAR(MAX) NOT NULL,
                    Version BIGINT NOT NULL DEFAULT 1,
                    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
                    CONSTRAINT PK_{_tableName} PRIMARY KEY (ActorId, StateName)
                );
                CREATE NONCLUSTERED INDEX IX_{_tableName}_UpdatedAt ON {_tableName}(UpdatedAt);
            END
        ";

        await using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT StateData FROM {_tableName} WHERE ActorId = @ActorId AND StateName = @StateName";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);
            command.Parameters.AddWithValue("@StateName", stateName);

            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            return json != null ? JsonSerializer.Deserialize<TState>(json, _jsonOptions) : null;
        });
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT StateData, Version FROM {_tableName} WHERE ActorId = @ActorId AND StateName = @StateName";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);
            command.Parameters.AddWithValue("@StateName", stateName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(0);
                var version = reader.GetInt64(1);
                var state = JsonSerializer.Deserialize<TState>(json, _jsonOptions);
                return state != null ? new StateWithVersion<TState>(state, version) : null;
            }

            return null;
        });
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var json = JsonSerializer.Serialize(state, _jsonOptions);
            var sql = $@"
                MERGE {_tableName} AS target
                USING (SELECT @ActorId AS ActorId, @StateName AS StateName) AS source
                ON (target.ActorId = source.ActorId AND target.StateName = source.StateName)
                WHEN MATCHED THEN
                    UPDATE SET StateData = @StateData, Version = Version + 1, UpdatedAt = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (ActorId, StateName, StateData, Version, UpdatedAt)
                    VALUES (@ActorId, @StateName, @StateData, 1, GETUTCDATE());
            ";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);
            command.Parameters.AddWithValue("@StateName", stateName);
            command.Parameters.AddWithValue("@StateData", json);

            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var json = JsonSerializer.Serialize(state, _jsonOptions);

            if (expectedVersion == null)
            {
                // First save - insert with version 1
                var sql = $@"
                    IF NOT EXISTS (SELECT 1 FROM {_tableName} WHERE ActorId = @ActorId AND StateName = @StateName)
                    BEGIN
                        INSERT INTO {_tableName} (ActorId, StateName, StateData, Version, UpdatedAt)
                        VALUES (@ActorId, @StateName, @StateData, 1, GETUTCDATE());
                        SELECT 1;
                    END
                    ELSE
                    BEGIN
                        SELECT NULL;
                    END
                ";

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ActorId", actorId);
                command.Parameters.AddWithValue("@StateName", stateName);
                command.Parameters.AddWithValue("@StateData", json);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result == null || result == DBNull.Value)
                {
                    // Conflict - state already exists, read actual version
                    var actualVersion = await GetCurrentVersionAsync(connection, actorId, stateName, cancellationToken);
                    throw new ConcurrencyException(0, actualVersion);
                }
                return 1L;
            }
            else
            {
                // Update with version check
                var sql = $@"
                    UPDATE {_tableName}
                    SET StateData = @StateData, Version = Version + 1, UpdatedAt = GETUTCDATE()
                    OUTPUT INSERTED.Version
                    WHERE ActorId = @ActorId AND StateName = @StateName AND Version = @ExpectedVersion
                ";

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ActorId", actorId);
                command.Parameters.AddWithValue("@StateName", stateName);
                command.Parameters.AddWithValue("@StateData", json);
                command.Parameters.AddWithValue("@ExpectedVersion", expectedVersion.Value);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result == null || result == DBNull.Value)
                {
                    // Version mismatch or not found
                    var actualVersion = await GetCurrentVersionAsync(connection, actorId, stateName, cancellationToken);
                    throw new ConcurrencyException(expectedVersion.Value, actualVersion);
                }
                return (long)result;
            }
        });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"DELETE FROM {_tableName} WHERE ActorId = @ActorId AND StateName = @StateName";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);
            command.Parameters.AddWithValue("@StateName", stateName);

            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    private async Task<long> GetCurrentVersionAsync(SqlConnection connection, string actorId, string stateName, CancellationToken cancellationToken)
    {
        var sql = $"SELECT Version FROM {_tableName} WHERE ActorId = @ActorId AND StateName = @StateName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ActorId", actorId);
        command.Parameters.AddWithValue("@StateName", stateName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? (long)result : 0L;
    }

    private static bool IsTransientError(SqlException ex)
    {
        // SQL Server transient error codes
        var transientErrorNumbers = new[]
        {
            -2,     // Timeout
            -1,     // Connection error
            2,      // Network error
            53,     // Connection error
            64,     // Server not found
            233,    // Connection initialization error
            10053,  // Transport-level error
            10054,  // Connection forcibly closed
            10060,  // Network timeout
            40197,  // Service error
            40501,  // Service busy
            40613,  // Database unavailable
            49918,  // Cannot process request
            49919,  // Cannot process create/update
            49920   // Cannot process delete
        };

        return transientErrorNumbers.Contains(ex.Number);
    }
}
