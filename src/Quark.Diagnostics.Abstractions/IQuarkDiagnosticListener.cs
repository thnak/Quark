namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Receives structured diagnostic events from the Quark runtime.
///     All methods have default no-op implementations — implement only the events you need.
///     Register via <c>services.AddQuarkDiagnostics&lt;TListener&gt;()</c>.
/// </summary>
public interface IQuarkDiagnosticListener
{
    // ── Grain lifecycle ──────────────────────────────────────────────────────

    void OnGrainActivating(in GrainActivatingEvent e) { }
    void OnGrainActivated(in GrainActivatedEvent e) { }
    void OnGrainDeactivating(in GrainDeactivatingEvent e) { }
    void OnGrainDeactivated(in GrainDeactivatedEvent e) { }

    // ── Invocations ──────────────────────────────────────────────────────────

    void OnInvocationStart(in InvocationStartEvent e) { }
    void OnInvocationEnd(in InvocationEndEvent e) { }

    // ── Mailbox / deadlock surface ────────────────────────────────────────────

    /// <summary>Called each time a work item is queued in a grain's mailbox.</summary>
    void OnMailboxEnqueued(in MailboxEnqueuedEvent e) { }

    /// <summary>
    ///     Called once when a grain's work item has been executing longer than
    ///     <see cref="DiagnosticOptions.StuckThreshold" />.
    /// </summary>
    void OnMailboxStuck(in MailboxStuckEvent e) { }

    /// <summary>Called when a previously-stuck grain becomes idle again.</summary>
    void OnMailboxStuckResolved(in MailboxStuckResolvedEvent e) { }

    // ── Gateway / network ────────────────────────────────────────────────────

    void OnConnectionAccepted(in ConnectionAcceptedEvent e) { }
    void OnConnectionClosed(in ConnectionClosedEvent e) { }
    void OnMessageDispatched(in MessageDispatchedEvent e) { }

    // ── Observer back-channel ─────────────────────────────────────────────────

    void OnObserverRegistered(in ObserverRegisteredEvent e) { }
    void OnObserverDeregistered(in ObserverDeregisteredEvent e) { }
    void OnObserverInvoked(in ObserverInvokedEvent e) { }

    // ── Timers ───────────────────────────────────────────────────────────────

    /// <summary>Called after a grain timer callback completes (successfully or with error).</summary>
    void OnTimerFired(in TimerFiredEvent e) { }

    // ── Cascading termination ─────────────────────────────────────────────────

    /// <summary>
    ///     Called when a best-effort cascade to a child grain cannot be delivered — the remote silo
    ///     is unreachable or the child's directory entry is stale.  The child is left independent.
    /// </summary>
    void OnChildTerminationFailed(in ChildTerminationFailedEvent e) { }
}
