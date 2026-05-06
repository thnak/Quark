using System.Buffers;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions;

namespace Quark.Persistence.Redis;

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