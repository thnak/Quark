using Npgsql;
using Quark.Abstractions;

namespace Quark.Messaging.Postgres;

/// <summary>
///     PostgreSQL-based implementation of the inbox pattern.
///     Tracks processed message IDs to ensure idempotent message processing.
/// </summary>
public sealed class PostgresInbox : IInbox
{
    private readonly string _connectionString;
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgresInbox"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing inbox records (default: "quark_inbox").</param>
    public PostgresInbox(string connectionString, string tableName = "quark_inbox")
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName;
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
                message_id VARCHAR(255) NOT NULL,
                processed_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                PRIMARY KEY (actor_id, message_id)
            );
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_processed_at ON {_tableName}(processed_at);
        ";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            SELECT COUNT(*) 
            FROM {_tableName}
            WHERE actor_id = @actorId AND message_id = @messageId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@messageId", messageId);

        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return count > 0;
    }

    /// <inheritdoc />
    public async Task MarkAsProcessedAsync(string actorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            INSERT INTO {_tableName} (actor_id, message_id, processed_at)
            VALUES (@actorId, @messageId, NOW())
            ON CONFLICT (actor_id, message_id) DO NOTHING";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@messageId", messageId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CleanupOldEntriesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            DELETE FROM {_tableName}
            WHERE processed_at < NOW() - @retentionPeriod::interval";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@retentionPeriod", retentionPeriod);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetProcessedAtAsync(
        string actorId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            SELECT processed_at
            FROM {_tableName}
            WHERE actor_id = @actorId AND message_id = @messageId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@messageId", messageId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DateTimeOffset timestamp ? timestamp : null;
    }
}
