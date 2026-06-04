namespace Quark.Reminders.Redis;

/// <summary>Configuration for the Redis reminder storage provider.</summary>
public sealed class RedisReminderOptions
{
    /// <summary>Redis connection string. Default: localhost:6379.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Redis Hash key under which all reminder entries are stored.</summary>
    public string HashKey { get; set; } = "quark:reminders";

    /// <summary>How often the polling service checks for due reminders. Default: 1 second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
