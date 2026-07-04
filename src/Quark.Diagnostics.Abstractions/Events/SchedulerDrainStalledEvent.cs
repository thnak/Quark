using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Fired by <see cref="StuckGrainDetector" /> when an activation has been rescheduled
///     <see cref="DiagnosticOptions.StalledDrainThreshold" /> consecutive times without a single
///     drain pass processing any work item, even though its mailbox still reports pending work.
///     Unlike <see cref="MailboxStuckEvent" /> (a single item running too long), this signals a
///     livelock: the scheduler keeps waking the activation up but it never makes progress —
///     typically because its cancellation token was triggered while queued work remained.
/// </summary>
public readonly struct SchedulerDrainStalledEvent(GrainId grainId, int consecutiveEmptyDrains, int pendingCount)
{
    public GrainId GrainId { get; } = grainId;

    /// <summary>Number of consecutive drain passes that processed zero items.</summary>
    public int ConsecutiveEmptyDrains { get; } = consecutiveEmptyDrains;

    /// <summary>Number of work items still queued behind the stalled drain.</summary>
    public int PendingCount { get; } = pendingCount;
}
