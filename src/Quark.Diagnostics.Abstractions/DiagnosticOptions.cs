namespace Quark.Diagnostics.Abstractions;

/// <summary>Configuration for the Quark diagnostic system.</summary>
public sealed class DiagnosticOptions
{
    /// <summary>
    ///     How long a grain's mailbox work item must run before
    ///     <see cref="IQuarkDiagnosticListener.OnMailboxStuck" /> is fired.
    ///     Default: 30 seconds.
    /// </summary>
    public TimeSpan StuckThreshold { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How often <see cref="StuckGrainDetector" /> polls active activations.
    ///     Default: 1 second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Number of consecutive scheduler drain passes that process zero items while the
    ///     activation's mailbox still reports pending work before
    ///     <see cref="IQuarkDiagnosticListener.OnSchedulerDrainStalled" /> is fired. Catches
    ///     livelocks — the scheduler keeps rescheduling an activation that never makes progress
    ///     (e.g. its cancellation token was triggered while queued work remained) rather than
    ///     hanging on a single await. Default: 20.
    /// </summary>
    public int StalledDrainThreshold { get; set; } = 20;

    /// <summary>
    ///     How long the activation scheduler may wait for its drain workers to finish during
    ///     shutdown before <see cref="IQuarkDiagnosticListener.OnSchedulerShutdownStalled" /> is
    ///     fired. The wait itself is not abandoned — this only surfaces that shutdown is taking
    ///     unusually long. Default: 10 seconds.
    /// </summary>
    public TimeSpan ShutdownStalledThreshold { get; set; } = TimeSpan.FromSeconds(10);
}
