using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions;
using StackExchange.Redis;

namespace Quark.Persistence.Redis;

/// <summary>
/// Configuration for the Redis grain storage provider.
/// </summary>
public sealed class RedisStorageOptions
{
    /// <summary>Redis connection string used to create the underlying multiplexer.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Logical key prefix for all persisted grain state entries.</summary>
    public string KeyPrefix { get; set; } = "quark:grainstate";

    /// <summary>The Redis database number to use.</summary>
    public int Database { get; set; }
}

/// <summary>
/// Serialized payload and ETag metadata stored for a grain state record.
/// </summary>
public readonly record struct RedisStorageRecord(byte[] Payload, string ETag);

/// <summary>
/// Minimal Redis access abstraction used by <see cref="RedisGrainStorage"/>.
/// </summary>
public interface IRedisStorageConnection
{
    /// <summary>Reads the stored record for <paramref name="key"/> if present.</summary>
    Task<RedisStorageRecord?> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="record"/> for <paramref name="key"/>.</summary>
    Task WriteAsync(string key, RedisStorageRecord record, CancellationToken cancellationToken = default);

    /// <summary>Deletes any record stored at <paramref name="key"/>.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// StackExchange.Redis-backed implementation of <see cref="IRedisStorageConnection"/>.
/// </summary>
public sealed class RedisStorageConnection : IRedisStorageConnection, IDisposable
{
    private static readonly RedisValue[] RequestedFields = ["payload", "etag"];
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;

    /// <summary>Creates a new Redis storage connection.</summary>
    public RedisStorageConnection(IOptions<RedisStorageOptions> options)
    {
        RedisStorageOptions value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ConnectionString);

        _multiplexer = ConnectionMultiplexer.Connect(value.ConnectionString);
        _database = _multiplexer.GetDatabase(value.Database);
    }

    /// <inheritdoc/>
    public async Task<RedisStorageRecord?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedisValue[] values = await _database.HashGetAsync(key, RequestedFields).ConfigureAwait(false);
        if (values.Length < 2 || values[0].IsNull)
        {
            return null;
        }

        byte[] payload = values[0]!;
        string eTag = values[1].IsNull ? string.Empty : values[1]!.ToString();
        return new RedisStorageRecord(payload, eTag);
    }

    /// <inheritdoc/>
    public Task WriteAsync(string key, RedisStorageRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HashEntry[] entries =
        [
            new("payload", record.Payload),
            new("etag", record.ETag)
        ];

        return _database.HashSetAsync(key, entries);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose() => _multiplexer.Dispose();
}

/// <summary>
/// Redis-backed implementation of <see cref="IGrainStorage"/>.
/// </summary>
public sealed class RedisGrainStorage : IGrainStorage
{
    private readonly IRedisStorageConnection _connection;
    private readonly ISerializer _serializer;
    private readonly RedisStorageOptions _options;

    /// <summary>Creates a new Redis grain storage provider.</summary>
    public RedisGrainStorage(
        IRedisStorageConnection connection,
        ISerializer serializer,
        IOptions<RedisStorageOptions> options)
    {
        _connection = connection;
        _serializer = serializer;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task ReadStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        RedisStorageRecord? record = await _connection.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (record is { } found)
        {
            TState? state = _serializer.Deserialize<TState>(found.Payload);
            grainState.State = state ?? new TState();
            grainState.RecordExists = true;
            grainState.ETag = found.ETag;
        }
        else
        {
            grainState.State = new TState();
            grainState.RecordExists = false;
            grainState.ETag = string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task WriteStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        ArrayBufferWriter<byte> buffer = new();
        _serializer.Serialize(buffer, grainState.State);

        string eTag = Guid.NewGuid().ToString("N");
        byte[] payload = buffer.WrittenSpan.ToArray();
        await _connection.WriteAsync(key, new RedisStorageRecord(payload, eTag), cancellationToken).ConfigureAwait(false);

        grainState.RecordExists = true;
        grainState.ETag = eTag;
    }

    /// <inheritdoc/>
    public async Task ClearStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        await _connection.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

        grainState.State = new TState();
        grainState.RecordExists = false;
        grainState.ETag = string.Empty;
    }

    private string GetStorageKey<TState>(string stateName, GrainId grainId) where TState : new()
    {
        string typeName = typeof(TState).FullName ?? typeof(TState).Name;
        return $"{_options.KeyPrefix}:{grainId.Type.Value}:{grainId.Key}:{stateName}:{typeName}";
    }
}

/// <summary>
/// Typed facade over <see cref="RedisGrainStorage"/> for a single state type.
/// </summary>
public sealed class RedisStorage<TState> : IStorage<TState>
    where TState : new()
{
    private readonly IGrainStorage _storage;

    /// <summary>Creates a typed Redis storage adapter.</summary>
    public RedisStorage(IGrainStorage storage)
    {
        _storage = storage;
    }

    /// <inheritdoc/>
    public async Task<TState> ReadAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> state = new();
        await _storage.ReadStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            state,
            cancellationToken).ConfigureAwait(false);

        return state.State;
    }

    /// <inheritdoc/>
    public Task WriteAsync(
        GrainId grainId,
        TState state,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> grainState = new() { State = state };
        return _storage.WriteStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            grainState,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task ClearAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> grainState = new();
        return _storage.ClearStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            grainState,
            cancellationToken);
    }
}

/// <summary>
/// Service registration helpers for the Redis persistence provider.
/// </summary>
public static class RedisGrainStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redis grain storage provider using optional options configuration.
    /// </summary>
    public static IServiceCollection AddRedisGrainStorage(
        this IServiceCollection services,
        Action<RedisStorageOptions>? configure = null)
    {
        services.AddOptions<RedisStorageOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IRedisStorageConnection, RedisStorageConnection>();
        services.TryAddSingleton<IGrainStorage, RedisGrainStorage>();
        services.TryAddSingleton(typeof(IStorage<>), typeof(RedisStorage<>));
        return services;
    }

    /// <summary>
    /// Registers the Redis grain storage provider using a connection string.
    /// </summary>
    public static IServiceCollection AddRedisGrainStorage(
        this IServiceCollection services,
        string connectionString,
        Action<RedisStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddRedisGrainStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }
}
