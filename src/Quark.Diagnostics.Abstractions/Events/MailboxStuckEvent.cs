using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

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