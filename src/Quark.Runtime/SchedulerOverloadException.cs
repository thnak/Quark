namespace Quark.Runtime;

/// <summary>
///     Thrown by <see cref="IActivationScheduler.ScheduleAsync"/> when a bounded scheduler ready queue
///     configured with <see cref="SchedulerOverloadMode.RejectWhenFull"/> is at capacity.
///     Signals that the silo's scheduler is overloaded so the caller can fail fast.
/// </summary>
public sealed class SchedulerOverloadException : Exception
{
    /// <summary>Initialises the exception for the given ready-queue <paramref name="capacity"/>.</summary>
    public SchedulerOverloadException(int capacity)
        : base($"Scheduler ready queue is full (capacity {capacity}); the silo is overloaded.")
    {
        Capacity = capacity;
    }

    /// <summary>The configured scheduler ready-queue capacity.</summary>
    public int Capacity { get; }
}
