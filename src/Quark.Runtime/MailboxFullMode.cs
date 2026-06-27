namespace Quark.Runtime;

/// <summary>
///     Policy applied when a bounded grain mailbox (<see cref="SiloRuntimeOptions.MailboxCapacity"/>
///     &gt; 0) reaches capacity.
/// </summary>
public enum MailboxFullMode
{
    /// <summary>
    ///     Block the caller until space frees up (backpressure). Propagates pressure back to the
    ///     transport read loop so an overloaded grain slows its callers rather than rejecting them.
    /// </summary>
    Wait = 0,

    /// <summary>
    ///     Reject the post immediately with <see cref="MailboxFullException"/> instead of waiting.
    ///     Use when callers should fail fast rather than stall on an overloaded grain.
    /// </summary>
    RejectWhenFull = 1,
}
