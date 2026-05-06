namespace Quark.Persistence.Redis;

/// <summary>
/// Serialized payload and ETag metadata stored for a grain state record.
/// </summary>
public readonly record struct RedisStorageRecord(byte[] Payload, string ETag);