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