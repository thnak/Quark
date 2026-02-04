namespace Quark.Abstractions;

/// <summary>
///     Configuration for supervision behavior.
/// </summary>
public sealed class SupervisionOptions
{
    /// <summary>
    ///     Gets or sets the restart strategy.
    ///     Default is OneForOne.
    /// </summary>
    public RestartStrategy RestartStrategy { get; set; } = RestartStrategy.OneForOne;

    /// <summary>
    ///     Gets or sets the maximum number of restarts within the time window.
    ///     Default is 3.
    /// </summary>
    public int MaxRestarts { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the time window for counting restarts.
    ///     Default is 60 seconds.
    /// </summary>
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Gets or sets the initial backoff duration before first restart.
    ///     Default is 1 second.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets the maximum backoff duration.
    ///     Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets or sets the backoff multiplier for exponential backoff.
    ///     Default is 2.0 (double the delay each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    ///     Gets or sets whether to escalate to parent if max restarts exceeded.
    ///     Default is true.
    /// </summary>
    public bool EscalateOnExceeded { get; set; } = true;
}