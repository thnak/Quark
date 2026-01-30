namespace Quark.Abstractions;

/// <summary>
///     Phase 8.3: Options for adaptive mailbox sizing to handle traffic bursts.
/// </summary>
public sealed class AdaptiveMailboxOptions
{
    /// <summary>
    ///     Gets or sets the initial mailbox capacity.
    ///     Default is 1000.
    /// </summary>
    public int InitialCapacity { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the minimum mailbox capacity.
    ///     The mailbox will never shrink below this size.
    ///     Default is 100.
    /// </summary>
    public int MinCapacity { get; set; } = 100;

    /// <summary>
    ///     Gets or sets the maximum mailbox capacity.
    ///     The mailbox will never grow beyond this size.
    ///     Default is 10000.
    /// </summary>
    public int MaxCapacity { get; set; } = 10000;

    /// <summary>
    ///     Gets or sets the threshold (percentage) at which to grow the mailbox.
    ///     When the mailbox is this full, capacity will be increased.
    ///     Default is 0.8 (80%).
    /// </summary>
    public double GrowThreshold { get; set; } = 0.8;

    /// <summary>
    ///     Gets or sets the threshold (percentage) at which to shrink the mailbox.
    ///     When the mailbox is consistently this empty, capacity will be decreased.
    ///     Default is 0.2 (20%).
    /// </summary>
    public double ShrinkThreshold { get; set; } = 0.2;

    /// <summary>
    ///     Gets or sets the growth factor when expanding capacity.
    ///     Default is 2.0 (double the capacity).
    /// </summary>
    public double GrowthFactor { get; set; } = 2.0;

    /// <summary>
    ///     Gets or sets the shrink factor when reducing capacity.
    ///     Default is 0.5 (halve the capacity).
    /// </summary>
    public double ShrinkFactor { get; set; } = 0.5;

    /// <summary>
    ///     Gets or sets the minimum number of samples required before adapting capacity.
    ///     This prevents rapid oscillation.
    ///     Default is 10 samples.
    /// </summary>
    public int MinSamplesBeforeAdapt { get; set; } = 10;

    /// <summary>
    ///     Gets or sets whether adaptive sizing is enabled.
    ///     Default is false (disabled by default for backward compatibility).
    /// </summary>
    public bool Enabled { get; set; } = false;
}

/// <summary>
///     Phase 8.3: Options for circuit breaker per actor type.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    ///     Gets or sets the failure threshold before opening the circuit.
    ///     Default is 5 consecutive failures.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    ///     Gets or sets the success threshold required to close the circuit from half-open state.
    ///     Default is 3 consecutive successes.
    /// </summary>
    public int SuccessThreshold { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the timeout before attempting to transition from open to half-open.
    ///     Default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets or sets the sampling window for failure rate calculation.
    ///     Only failures within this window are counted.
    ///     Default is 60 seconds.
    /// </summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Gets or sets whether circuit breaker is enabled.
    ///     Default is false (disabled by default for backward compatibility).
    /// </summary>
    public bool Enabled { get; set; } = false;
}

/// <summary>
///     Phase 8.3: Circuit breaker state.
/// </summary>
public enum CircuitState
{
    /// <summary>
    ///     Circuit is closed - requests flow normally.
    /// </summary>
    Closed,

    /// <summary>
    ///     Circuit is open - requests are rejected immediately.
    /// </summary>
    Open,

    /// <summary>
    ///     Circuit is half-open - limited requests are allowed to test if the issue is resolved.
    /// </summary>
    HalfOpen
}

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
