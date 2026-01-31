using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Quark.Abstractions.Persistence;

namespace Quark.Storage.DynamoDB;

/// <summary>
///     DynamoDB-based implementation of state storage with optimistic concurrency control.
///     Supports on-demand capacity mode and global tables for multi-region deployments.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class DynamoDbStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DynamoDbStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="client">The DynamoDB client instance.</param>
    /// <param name="tableName">The table name for storing state (default: "QuarkState").</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public DynamoDbStateStorage(
        IAmazonDynamoDB client,
        string tableName = "QuarkState",
        JsonSerializerOptions? jsonOptions = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _tableName = tableName;
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
                    new() { AttributeName = "StateName", AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = "UpdatedAt", AttributeType = ScalarAttributeType.N }
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new() { AttributeName = "ActorId", KeyType = KeyType.HASH },
                    new() { AttributeName = "StateName", KeyType = KeyType.RANGE }
                },
                BillingMode = actualBillingMode,
                GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
                {
                    new()
                    {
                        IndexName = "UpdatedAtIndex",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new() { AttributeName = "ActorId", KeyType = KeyType.HASH },
                            new() { AttributeName = "UpdatedAt", KeyType = KeyType.RANGE }
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
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["StateName"] = new AttributeValue { S = stateName }
            }
        };

        var response = await _client.GetItemAsync(request, cancellationToken);

        if (!response.IsItemSet || !response.Item.ContainsKey("StateData"))
            return null;

        var json = response.Item["StateData"].S;
        return JsonSerializer.Deserialize<TState>(json, _jsonOptions);
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["StateName"] = new AttributeValue { S = stateName }
            }
        };

        var response = await _client.GetItemAsync(request, cancellationToken);

        if (!response.IsItemSet || !response.Item.ContainsKey("StateData"))
            return null;

        var json = response.Item["StateData"].S;
        var version = long.Parse(response.Item["Version"].N);
        var state = JsonSerializer.Deserialize<TState>(json, _jsonOptions);

        return state != null ? new StateWithVersion<TState>(state, version) : null;
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var currentVersion = 0L;

        // Try to get current version
        var getRequest = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["StateName"] = new AttributeValue { S = stateName }
            },
            ProjectionExpression = "Version"
        };

        var getResponse = await _client.GetItemAsync(getRequest, cancellationToken);
        if (getResponse.IsItemSet && getResponse.Item.ContainsKey("Version"))
        {
            currentVersion = long.Parse(getResponse.Item["Version"].N);
        }

        var putRequest = new PutItemRequest
        {
            TableName = _tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["StateName"] = new AttributeValue { S = stateName },
                ["StateData"] = new AttributeValue { S = json },
                ["Version"] = new AttributeValue { N = (currentVersion + 1).ToString() },
                ["UpdatedAt"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
            }
        };

        await _client.PutItemAsync(putRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);

        if (expectedVersion == null)
        {
            // First save - use conditional put
            var putRequest = new PutItemRequest
            {
                TableName = _tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["ActorId"] = new AttributeValue { S = actorId },
                    ["StateName"] = new AttributeValue { S = stateName },
                    ["StateData"] = new AttributeValue { S = json },
                    ["Version"] = new AttributeValue { N = "1" },
                    ["UpdatedAt"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
                },
                ConditionExpression = "attribute_not_exists(ActorId)"
            };

            try
            {
                await _client.PutItemAsync(putRequest, cancellationToken);
                return 1L;
            }
            catch (ConditionalCheckFailedException)
            {
                // Conflict - state already exists
                var existing = await LoadWithVersionAsync(actorId, stateName, cancellationToken);
                var actualVersion = existing?.Version ?? 0L;
                throw new ConcurrencyException(0, actualVersion);
            }
        }
        else
        {
            // Update with version check
            var newVersion = expectedVersion.Value + 1;
            var updateRequest = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["ActorId"] = new AttributeValue { S = actorId },
                    ["StateName"] = new AttributeValue { S = stateName }
                },
                UpdateExpression = "SET StateData = :stateData, Version = :newVersion, UpdatedAt = :updatedAt",
                ConditionExpression = "Version = :expectedVersion",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":stateData"] = new AttributeValue { S = json },
                    [":newVersion"] = new AttributeValue { N = newVersion.ToString() },
                    [":expectedVersion"] = new AttributeValue { N = expectedVersion.Value.ToString() },
                    [":updatedAt"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
                }
            };

            try
            {
                await _client.UpdateItemAsync(updateRequest, cancellationToken);
                return newVersion;
            }
            catch (ConditionalCheckFailedException)
            {
                // Version mismatch
                var existing = await LoadWithVersionAsync(actorId, stateName, cancellationToken);
                var actualVersion = existing?.Version ?? 0L;
                throw new ConcurrencyException(expectedVersion.Value, actualVersion);
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ActorId"] = new AttributeValue { S = actorId },
                ["StateName"] = new AttributeValue { S = stateName }
            }
        };

        await _client.DeleteItemAsync(request, cancellationToken);
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
