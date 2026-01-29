using System.Text.Json;
using Npgsql;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Storage.Postgres;

/// <summary>
///     PostgreSQL-based implementation of persistent reminder storage.
///     Uses a table with indexed columns for efficient time-based queries.
/// </summary>
public sealed class PostgresReminderTable : IReminderTable
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly IConsistentHashRing? _hashRing;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgresReminderTable"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    /// <param name="tableName">The table name for storing reminders (default: "quark_reminders").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public PostgresReminderTable(
        string connectionString,
        IConsistentHashRing? hashRing = null,
        string tableName = "quark_reminders",
        JsonSerializerOptions? jsonOptions = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _hashRing = hashRing;
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
                actor_type VARCHAR(255) NOT NULL,
                name VARCHAR(255) NOT NULL,
                due_time TIMESTAMP WITH TIME ZONE NOT NULL,
                period INTERVAL,
                data JSONB NOT NULL,
                last_fired_at TIMESTAMP WITH TIME ZONE,
                next_fire_time TIMESTAMP WITH TIME ZONE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                PRIMARY KEY (actor_id, name)
            );
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_next_fire_time ON {_tableName}(next_fire_time);
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_actor_type ON {_tableName}(actor_type);
        ";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var json = JsonSerializer.Serialize(reminder, _jsonOptions);
        var sql = $@"
            INSERT INTO {_tableName} (actor_id, actor_type, name, due_time, period, data, last_fired_at, next_fire_time, created_at)
            VALUES (@actorId, @actorType, @name, @dueTime, @period, @data::jsonb, @lastFiredAt, @nextFireTime, NOW())
            ON CONFLICT (actor_id, name)
            DO UPDATE SET 
                actor_type = @actorType,
                due_time = @dueTime,
                period = @period,
                data = @data::jsonb,
                last_fired_at = @lastFiredAt,
                next_fire_time = @nextFireTime
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", reminder.ActorId);
        command.Parameters.AddWithValue("@actorType", reminder.ActorType);
        command.Parameters.AddWithValue("@name", reminder.Name);
        command.Parameters.AddWithValue("@dueTime", reminder.DueTime);
        command.Parameters.AddWithValue("@period", (object?)reminder.Period ?? DBNull.Value);
        command.Parameters.AddWithValue("@data", json);
        command.Parameters.AddWithValue("@lastFiredAt", (object?)reminder.LastFiredAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@nextFireTime", reminder.NextFireTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE actor_id = @actorId AND name = @name";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@name", name);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT data FROM {_tableName} WHERE actor_id = @actorId";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);

        var reminders = new List<Reminder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
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
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT data FROM {_tableName} WHERE next_fire_time <= @utcNow";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@utcNow", utcNow);

        var reminders = new List<Reminder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
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
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $@"
            UPDATE {_tableName}
            SET last_fired_at = @lastFiredAt, next_fire_time = @nextFireTime
            WHERE actor_id = @actorId AND name = @name
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@lastFiredAt", lastFiredAt);
        command.Parameters.AddWithValue("@nextFireTime", nextFireTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
    }
}
