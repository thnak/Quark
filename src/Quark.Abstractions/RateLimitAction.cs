namespace Quark.Abstractions;

/// <summary>
///     Phase 8.3: Action to take when rate limit is exceeded.
/// </summary>
public enum RateLimitAction
{
    /// <summary>
    ///     Drop excess messages silently.
    /// </summary>
    Drop,

    /// <summary>
    ///     Reject excess messages with an exception.
    /// </summary>
    Reject,

    /// <summary>
    ///     Queue excess messages and process them later (backpressure).
    /// </summary>
    Queue
}