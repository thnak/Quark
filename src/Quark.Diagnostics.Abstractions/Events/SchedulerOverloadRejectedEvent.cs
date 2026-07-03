namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Fired when the scheduler rejects an activation because its bounded ready queue is full
///     and the overload mode is <see cref="Quark.Runtime.SchedulerOverloadMode.RejectWhenFull"/>.
/// </summary>
public readonly struct SchedulerOverloadRejectedEvent(int queueCapacity)
{
    /// <summary>The configured scheduler ready-queue capacity.</summary>
    public int QueueCapacity { get; } = queueCapacity;
}
