namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired each time the scheduler ready-queue depth changes (activation added or removed).</summary>
public readonly struct SchedulerReadyQueueDepthChangedEvent(int depth, int delta)
{
    /// <summary>Current ready-queue depth after the change.</summary>
    public int Depth { get; } = depth;
    /// <summary><c>+1</c> when an activation was added; <c>-1</c> when one was removed.</summary>
    public int Delta { get; } = delta;
}
