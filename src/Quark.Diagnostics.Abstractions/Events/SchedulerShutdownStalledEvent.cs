namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Fired when the activation scheduler's shutdown (waiting for all drain workers to finish)
///     has been running longer than <see cref="DiagnosticOptions.ShutdownStalledThreshold" />.
///     The scheduler keeps waiting — this only surfaces that silo shutdown is stuck rather than
///     merely slow, since a hung drain worker otherwise blocks host shutdown indefinitely with no
///     other observable signal.
/// </summary>
public readonly struct SchedulerShutdownStalledEvent(int pendingWorkerCount, int totalWorkerCount, TimeSpan elapsed)
{
    /// <summary>Number of drain worker tasks that have not yet completed.</summary>
    public int PendingWorkerCount { get; } = pendingWorkerCount;

    /// <summary>Total number of drain worker tasks the scheduler started.</summary>
    public int TotalWorkerCount { get; } = totalWorkerCount;

    /// <summary>How long shutdown has been waiting on the pending workers so far.</summary>
    public TimeSpan Elapsed { get; } = elapsed;
}
