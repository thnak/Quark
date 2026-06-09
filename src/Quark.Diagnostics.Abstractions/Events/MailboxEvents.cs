using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired each time a work item is enqueued in a grain's mailbox.</summary>
public readonly struct MailboxEnqueuedEvent(GrainId grainId, int pendingCount)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>Number of work items pending in the mailbox after this enqueue.</summary>
    public int PendingCount { get; } = pendingCount;
}

/// <summary>
///     Fired by <see cref="StuckGrainDetector" /> when a grain's current work item has been
///     running longer than <see cref="DiagnosticOptions.StuckThreshold" />.
///     Fired once per stuck event, not on every poll cycle.
/// </summary>
public readonly struct MailboxStuckEvent(GrainId grainId, TimeSpan runningFor, int pendingCount)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>How long the current work item has been executing.</summary>
    public TimeSpan RunningFor { get; } = runningFor;
    /// <summary>Number of work items queued behind the stuck item.</summary>
    public int PendingCount { get; } = pendingCount;
}

/// <summary>
///     Fired by <see cref="StuckGrainDetector" /> when a previously-stuck grain becomes idle again
///     (its work item completed).
/// </summary>
public readonly struct MailboxStuckResolvedEvent(GrainId grainId, TimeSpan totalStuckDuration)
{
    public GrainId GrainId { get; } = grainId;
    public TimeSpan TotalStuckDuration { get; } = totalStuckDuration;
}
