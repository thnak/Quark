namespace Quark.Persistence.Redis;

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