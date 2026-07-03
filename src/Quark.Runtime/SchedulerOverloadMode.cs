namespace Quark.Runtime;

/// <summary>
///     Policy applied when a bounded scheduler ready queue is full.
///     See <see cref="SiloRuntimeOptions.SchedulerOverloadMode"/>.
/// </summary>
public enum SchedulerOverloadMode
{
    /// <summary>Block the caller until a slot opens in the scheduler ready queue (backpressure).</summary>
    Wait = 0,

    /// <summary>
    ///     Immediately throw <see cref="SchedulerOverloadException"/> when the scheduler ready queue is
    ///     full. The caller fails fast rather than queueing unbounded work.
    /// </summary>
    RejectWhenFull = 1,
}
