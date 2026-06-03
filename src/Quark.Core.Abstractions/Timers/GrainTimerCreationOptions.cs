namespace Quark.Core.Abstractions.Timers;

/// <summary>
///     Options for timers created via <c>RegisterGrainTimer</c>.
///     Drop-in equivalent of Orleans' <c>GrainTimerCreationOptions</c>.
/// </summary>
public sealed class GrainTimerCreationOptions
{
    /// <summary>Delay before the first fire. Defaults to <see cref="TimeSpan.Zero" />.</summary>
    public TimeSpan DueTime { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     Interval between subsequent fires.
    ///     Use <see cref="Timeout.InfiniteTimeSpan" /> for one-shot timers.
    /// </summary>
    public TimeSpan Period { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    ///     When <c>true</c>, the timer callback may fire even while the grain
    ///     is still executing a previous timer callback (interleaved).
    ///     When <c>false</c> (default), a pending fire is skipped if the previous one has not finished.
    /// </summary>
    public bool Interleave { get; init; }
}
