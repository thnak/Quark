using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Quark.Persistence.Redis;

/// <summary>
///     StackExchange.Redis-backed implementation of <see cref="IRedisStorageConnection" />.
/// </summary>
public sealed class RedisStorageConnection : IRedisStorageConnection, IDisposable
{
    private static readonly RedisValue[] RequestedFields = ["payload", "etag"];
    private readonly IDatabase _database;
    private readonly ConnectionMultiplexer _multiplexer;

    /// <summary>Creates a new Redis storage connection.</summary>
    public RedisStorageConnection(IOptions<RedisStorageOptions> options)
    {
        RedisStorageOptions value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ConnectionString);

        _multiplexer = ConnectionMultiplexer.Connect(value.ConnectionString);
        _database = _multiplexer.GetDatabase(value.Database);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _multiplexer.Dispose();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(key).ConfigureAwait(false);
    }
}
