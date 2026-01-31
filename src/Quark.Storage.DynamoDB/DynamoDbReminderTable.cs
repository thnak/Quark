using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Storage.DynamoDB;

/// <summary>
///     DynamoDB-based implementation of persistent reminder storage.
///     Supports global tables for multi-region deployments and on-demand capacity.
/// </summary>
public sealed class DynamoDbReminderTable : IReminderTable
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;
    private readonly IConsistentHashRing? _hashRing;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DynamoDbReminderTable"/> class.
    /// </summary>
    /// <param name="client">The DynamoDB client instance.</param>
    /// <param name="tableName">The table name for storing reminders (default: "QuarkReminders").</param>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public DynamoDbReminderTable(
        IAmazonDynamoDB client,
        string tableName = "QuarkReminders",
        IConsistentHashRing? hashRing = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _tableName = tableName;
        _hashRing = hashRing;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    ///     Initializes the DynamoDB table. Call this once during application startup.
    /// </summary>
    /// <param name="billingMode">Billing mode (if null, defaults to PAY_PER_REQUEST for on-demand).</param>
    /// <param name="enablePointInTimeRecovery">Enable point-in-time recovery (default: true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeTableAsync(
        BillingMode? billingMode = null,
        bool enablePointInTimeRecovery = true,
        CancellationToken cancellationToken = default)
    {
        var actualBillingMode = billingMode ?? BillingMode.PAY_PER_REQUEST;
        try
        {
            // Check if table exists
            await _client.DescribeTableAsync(_tableName, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            // Table doesn't exist, create it
            var request = new CreateTableRequest
            {
                TableName = _tableName,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new() { AttributeName = "ActorId", AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = "Name", AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = "NextFireTime", AttributeType = ScalarAttributeType.N }
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new() { AttributeName = "ActorId", KeyType = KeyType.HASH },
                    new() { AttributeName = "Name", KeyType = KeyType.RANGE }
                },
                BillingMode = actualBillingMode,
                GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
                {
                    new()
                    {
                        IndexName = "NextFireTimeIndex",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new() { AttributeName = "ActorId", KeyType = KeyType.HASH },
                            new() { AttributeName = "NextFireTime", KeyType = KeyType.RANGE }
                        },
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    }
                }
            };

            if (actualBillingMode == BillingMode.PROVISIONED)
            {
                request.ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 5,
                    WriteCapacityUnits = 5
                };
                request.GlobalSecondaryIndexes[0].ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 5,
                    WriteCapacityUnits = 5
                };
            }

            await _client.CreateTableAsync(request, cancellationToken);

            // Wait for table to be active
            await WaitForTableActiveAsync(cancellationToken);
        }

        // Enable point-in-time recovery if requested
        if (enablePointInTimeRecovery)
        {
            await _client.UpdateContinuousBackupsAsync(new UpdateContinuousBackupsRequest
            {
                TableName = _tableName,
                PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
                {
                    PointInTimeRecoveryEnabled = true
                }
            }, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(reminder, _jsonOptions);

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = reminder.ActorId },
                ["Name"] = new AttributeValue { S = reminder.Name },
                ["ActorType"] = new AttributeValue { S = reminder.ActorType },
                ["DueTime"] = new AttributeValue { N = reminder.DueTime.ToUnixTimeMilliseconds().ToString() },
                ["NextFireTime"] = new AttributeValue { N = reminder.NextFireTime.ToUnixTimeMilliseconds().ToString() },
                ["ReminderData"] = new AttributeValue { S = json }
            }
        };

        if (reminder.Period.HasValue)
        {
            request.Item["PeriodTicks"] = new AttributeValue { N = reminder.Period.Value.Ticks.ToString() };
        }

        if (reminder.LastFiredAt.HasValue)
        {
            request.Item["LastFiredAt"] = new AttributeValue { N = reminder.LastFiredAt.Value.ToUnixTimeMilliseconds().ToString() };
        }

        await _client.PutItemAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        var request = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["Name"] = new AttributeValue { S = name }
            }
        };

        await _client.DeleteItemAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var request = new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "ActorId = :actorId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":actorId"] = new AttributeValue { S = actorId }
            }
        };

        var response = await _client.QueryAsync(request, cancellationToken);

        var reminders = new List<Reminder>();
        foreach (var item in response.Items)
        {
            if (item.ContainsKey("ReminderData"))
            {
                var json = item["ReminderData"].S;
                var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
                if (reminder != null)
                {
                    reminders.Add(reminder);
                }
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
        // DynamoDB doesn't support scanning across all partitions efficiently,
        // so we need to scan the table. For production, consider using DynamoDB Streams
        // or a separate index with a fixed partition key for time-based queries.
        var request = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "NextFireTime <= :now",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":now"] = new AttributeValue { N = utcNow.ToUnixTimeMilliseconds().ToString() }
            }
        };

        var reminders = new List<Reminder>();
        var response = await _client.ScanAsync(request, cancellationToken);

        foreach (var item in response.Items)
        {
            if (item.ContainsKey("ReminderData"))
            {
                var json = item["ReminderData"].S;
                var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
                if (reminder != null && IsReminderOwnedBySilo(reminder, siloId))
                {
                    reminders.Add(reminder);
                }
            }
        }

        // Handle pagination if there are more results
        while (!string.IsNullOrEmpty(response.LastEvaluatedKey?.FirstOrDefault().Value?.S))
        {
            request.ExclusiveStartKey = response.LastEvaluatedKey;
            response = await _client.ScanAsync(request, cancellationToken);

            foreach (var item in response.Items)
            {
                if (item.ContainsKey("ReminderData"))
                {
                    var json = item["ReminderData"].S;
                    var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);
                    if (reminder != null && IsReminderOwnedBySilo(reminder, siloId))
                    {
                        reminders.Add(reminder);
                    }
                }
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
        var getRequest = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["Name"] = new AttributeValue { S = name }
            }
        };

        var getResponse = await _client.GetItemAsync(getRequest, cancellationToken);

        if (!getResponse.IsItemSet || !getResponse.Item.ContainsKey("ReminderData"))
            return;

        var json = getResponse.Item["ReminderData"].S;
        var reminder = JsonSerializer.Deserialize<Reminder>(json, _jsonOptions);

        if (reminder != null)
        {
            reminder.LastFiredAt = lastFiredAt;
            reminder.NextFireTime = nextFireTime;

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

    private async Task WaitForTableActiveAsync(CancellationToken cancellationToken)
    {
        var maxWaitTime = TimeSpan.FromMinutes(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            var response = await _client.DescribeTableAsync(_tableName, cancellationToken);
            if (response.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException($"Table {_tableName} did not become active within {maxWaitTime}");
    }
}
