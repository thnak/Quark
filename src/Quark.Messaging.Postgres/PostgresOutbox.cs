using System.Text.Json;
using Npgsql;
using Quark.Abstractions;

namespace Quark.Messaging.Postgres;

/// <summary>
///     PostgreSQL-based implementation of the outbox pattern.
///     Provides transactional message delivery guarantees.
/// </summary>
public sealed class PostgresOutbox : IOutbox
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgresOutbox"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing outbox messages (default: "quark_outbox").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public PostgresOutbox(string connectionString, string tableName = "quark_outbox", JsonSerializerOptions? jsonOptions = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName;
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

        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {_tableName} (
                message_id VARCHAR(255) PRIMARY KEY,
                actor_id VARCHAR(255) NOT NULL,
                destination VARCHAR(255) NOT NULL,
                message_type VARCHAR(255) NOT NULL,
                payload BYTEA NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                sent_at TIMESTAMP WITH TIME ZONE,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                last_error TEXT,
                next_retry_at TIMESTAMP WITH TIME ZONE
            );
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_actor_id ON {_tableName}(actor_id);
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_created_at ON {_tableName}(created_at);
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_pending ON {_tableName}(created_at) 
                WHERE sent_at IS NULL AND retry_count < max_retries;
        ";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(message.MessageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            INSERT INTO {_tableName} 
                (message_id, actor_id, destination, message_type, payload, created_at, retry_count, max_retries)
            VALUES 
                (@messageId, @actorId, @destination, @messageType, @payload, @createdAt, @retryCount, @maxRetries)";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@messageId", message.MessageId);
        command.Parameters.AddWithValue("@actorId", message.ActorId);
        command.Parameters.AddWithValue("@destination", message.Destination);
        command.Parameters.AddWithValue("@messageType", message.MessageType);
        command.Parameters.AddWithValue("@payload", message.Payload);
        command.Parameters.AddWithValue("@createdAt", message.CreatedAt);
        command.Parameters.AddWithValue("@retryCount", message.RetryCount);
        command.Parameters.AddWithValue("@maxRetries", message.MaxRetries);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            SELECT message_id, actor_id, destination, message_type, payload, created_at, 
                   sent_at, retry_count, max_retries, last_error, next_retry_at
            FROM {_tableName}
            WHERE sent_at IS NULL 
              AND retry_count < max_retries
              AND (next_retry_at IS NULL OR next_retry_at <= NOW())
            ORDER BY created_at
            LIMIT @batchSize";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batchSize", batchSize);

        var messages = new List<OutboxMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage
            {
                MessageId = reader.GetString(0),
                ActorId = reader.GetString(1),
                Destination = reader.GetString(2),
                MessageType = reader.GetString(3),
                Payload = (byte[])reader.GetValue(4),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                SentAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                RetryCount = reader.GetInt32(7),
                MaxRetries = reader.GetInt32(8),
                LastError = reader.IsDBNull(9) ? null : reader.GetString(9),
                NextRetryAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)
            });
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task MarkAsSentAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"UPDATE {_tableName} SET sent_at = NOW() WHERE message_id = @messageId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@messageId", messageId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            UPDATE {_tableName} 
            SET retry_count = retry_count + 1,
                last_error = @error,
                next_retry_at = NOW() + (INTERVAL '1 second' * POWER(2, retry_count + 1))
            WHERE message_id = @messageId";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@error", error ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CleanupSentMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            DELETE FROM {_tableName}
            WHERE sent_at IS NOT NULL
              AND sent_at < NOW() - @retentionPeriod::interval";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@retentionPeriod", retentionPeriod);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
