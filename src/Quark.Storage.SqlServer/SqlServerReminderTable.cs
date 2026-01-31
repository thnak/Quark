using System.Text.Json;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Storage.SqlServer;

/// <summary>
///     SQL Server-based implementation of persistent reminder storage.
///     Uses a table with indexes for efficient time-based and actor-based queries.
/// </summary>
public sealed class SqlServerReminderTable : IReminderTable
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly IConsistentHashRing? _hashRing;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AsyncRetryPolicy _retryPolicy;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerReminderTable"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="tableName">The table name for storing reminders (default: "QuarkReminders").</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="retryPolicy">Optional custom retry policy. If null, uses default exponential backoff.</param>
    public SqlServerReminderTable(
        string connectionString,
        string tableName = "QuarkReminders",
        IConsistentHashRing? hashRing = null,
        JsonSerializerOptions? jsonOptions = null,
        AsyncRetryPolicy? retryPolicy = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName;
        _hashRing = hashRing;
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
                    Name NVARCHAR(255) NOT NULL,
                    ActorType NVARCHAR(255) NOT NULL,
                    DueTime DATETIMEOFFSET NOT NULL,
                    Period BIGINT NULL,
                    NextFireTime DATETIMEOFFSET NOT NULL,
                    LastFiredAt DATETIMEOFFSET NULL,
                    ReminderData NVARCHAR(MAX) NULL,
                    CONSTRAINT PK_{_tableName} PRIMARY KEY (ActorId, Name)
                );
                CREATE NONCLUSTERED INDEX IX_{_tableName}_NextFireTime ON {_tableName}(NextFireTime);
                CREATE NONCLUSTERED INDEX IX_{_tableName}_ActorType ON {_tableName}(ActorType);
            END
        ";

        await using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                MERGE {_tableName} AS target
                USING (SELECT @ActorId AS ActorId, @Name AS Name) AS source
                ON (target.ActorId = source.ActorId AND target.Name = source.Name)
                WHEN MATCHED THEN
                    UPDATE SET 
                        DueTime = @DueTime,
                        Period = @Period,
                        NextFireTime = @NextFireTime,
                        LastFiredAt = @LastFiredAt,
                        ReminderData = @ReminderData
                WHEN NOT MATCHED THEN
                    INSERT (ActorId, Name, ActorType, DueTime, Period, NextFireTime, LastFiredAt, ReminderData)
                    VALUES (@ActorId, @Name, @ActorType, @DueTime, @Period, @NextFireTime, @LastFiredAt, @ReminderData);
            ";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", reminder.ActorId);
            command.Parameters.AddWithValue("@Name", reminder.Name);
            command.Parameters.AddWithValue("@ActorType", reminder.ActorType);
            command.Parameters.AddWithValue("@DueTime", reminder.DueTime);
            command.Parameters.AddWithValue("@Period", (object?)reminder.Period?.Ticks ?? DBNull.Value);
            command.Parameters.AddWithValue("@NextFireTime", reminder.NextFireTime);
            command.Parameters.AddWithValue("@LastFiredAt", (object?)reminder.LastFiredAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReminderData", JsonSerializer.Serialize(reminder, _jsonOptions));

            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"DELETE FROM {_tableName} WHERE ActorId = @ActorId AND Name = @Name";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);
            command.Parameters.AddWithValue("@Name", name);

            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT ReminderData FROM {_tableName} WHERE ActorId = @ActorId";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ActorId", actorId);

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
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetDueRemindersForSiloAsync(
        string siloId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT ReminderData FROM {_tableName} WHERE NextFireTime <= @UtcNow";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UtcNow", utcNow);

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
        });
    }

    /// <inheritdoc />
    public async Task UpdateFireTimeAsync(
        string actorId,
        string name,
        DateTimeOffset lastFiredAt,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // First, get the current reminder to update the full data
            var selectSql = $"SELECT ReminderData FROM {_tableName} WHERE ActorId = @ActorId AND Name = @Name";
            await using var selectCommand = new SqlCommand(selectSql, connection);
            selectCommand.Parameters.AddWithValue("@ActorId", actorId);
            selectCommand.Parameters.AddWithValue("@Name", name);

            var json = await selectCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (json == null)
                return;

            var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
            if (reminder != null)
            {
                reminder.LastFiredAt = lastFiredAt;
                reminder.NextFireTime = nextFireTime;

                var updateSql = $@"
                    UPDATE {_tableName}
                    SET NextFireTime = @NextFireTime, LastFiredAt = @LastFiredAt, ReminderData = @ReminderData
                    WHERE ActorId = @ActorId AND Name = @Name
                ";

                await using var updateCommand = new SqlCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("@ActorId", actorId);
                updateCommand.Parameters.AddWithValue("@Name", name);
                updateCommand.Parameters.AddWithValue("@NextFireTime", nextFireTime);
                updateCommand.Parameters.AddWithValue("@LastFiredAt", lastFiredAt);
                updateCommand.Parameters.AddWithValue("@ReminderData", JsonSerializer.Serialize(reminder, _jsonOptions));

                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        });
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
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
