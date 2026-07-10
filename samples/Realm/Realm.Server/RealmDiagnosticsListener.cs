using Microsoft.Extensions.Logging;
using Quark.Diagnostics.Abstractions;

namespace Realm.Server;

/// <summary>
///     Logs the hang-detection surface exposed by <c>Quark.Diagnostics.StuckGrainDetector</c>
///     (<c>AddQuarkStuckGrainDetector()</c>) so a stuck map/player activation shows up in the
///     silo's console output instead of silently degrading throughput. See "Diagnostics" in
///     CLAUDE.md for the full event surface this could be extended to cover.
/// </summary>
public sealed class RealmDiagnosticsListener(ILogger<RealmDiagnosticsListener> logger) : IQuarkDiagnosticListener
{
    public void OnMailboxStuck(in MailboxStuckEvent e) =>
        logger.LogWarning(
            "[diagnostics] {GrainId} mailbox stuck: work item running {RunningFor:g}, {Pending} item(s) queued behind it.",
            e.GrainId, e.RunningFor, e.PendingCount);

    public void OnMailboxStuckResolved(in MailboxStuckResolvedEvent e) =>
        logger.LogInformation(
            "[diagnostics] {GrainId} mailbox unstuck after {Duration:g}.",
            e.GrainId, e.TotalStuckDuration);

    public void OnSchedulerDrainStalled(in SchedulerDrainStalledEvent e) =>
        logger.LogWarning(
            "[diagnostics] {GrainId} scheduler drain livelocked: {Count} consecutive empty drain passes, {Pending} item(s) still queued.",
            e.GrainId, e.ConsecutiveEmptyDrains, e.PendingCount);

    public void OnSchedulerShutdownStalled(in SchedulerShutdownStalledEvent e) =>
        logger.LogWarning(
            "[diagnostics] scheduler shutdown stalled: {Pending}/{Total} drain worker(s) still running after {Elapsed:g}.",
            e.PendingWorkerCount, e.TotalWorkerCount, e.Elapsed);

    public void OnSchedulerOverloadRejected(in SchedulerOverloadRejectedEvent e) =>
        logger.LogWarning(
            "[diagnostics] scheduler ready queue full (capacity {Capacity}) -- activation rejected.",
            e.QueueCapacity);
}
