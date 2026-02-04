namespace Quark.Abstractions;

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