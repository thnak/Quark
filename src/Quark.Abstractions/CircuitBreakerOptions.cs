namespace Quark.Abstractions;

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