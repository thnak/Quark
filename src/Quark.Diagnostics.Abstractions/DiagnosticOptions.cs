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
}
