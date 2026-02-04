namespace Quark.Abstractions;

/// <summary>
///     Phase 8.3: Options for rate limiting per actor type.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    ///     Gets or sets the maximum number of messages per time window.
    ///     Default is 1000 messages.
    /// </summary>
    public int MaxMessagesPerWindow { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the time window for rate limiting.
    ///     Default is 1 second.
    /// </summary>
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets the action to take when rate limit is exceeded.
    ///     Default is Drop (silently drop excess messages).
    /// </summary>
    public RateLimitAction ExcessAction { get; set; } = RateLimitAction.Drop;

    /// <summary>
    ///     Gets or sets whether rate limiting is enabled.
    ///     Default is false (disabled by default for backward compatibility).
    /// </summary>
    public bool Enabled { get; set; } = false;
}