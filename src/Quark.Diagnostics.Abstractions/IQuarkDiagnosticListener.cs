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

    // ── Scheduler ────────────────────────────────────────────────────────────

    /// <summary>Called each time the scheduler ready-queue depth changes (activation added or removed).</summary>
    void OnSchedulerReadyQueueDepthChanged(in SchedulerReadyQueueDepthChangedEvent e) { }

    /// <summary>Called when an activation is successfully added to the scheduler ready queue.</summary>
    void OnSchedulerActivationScheduled(in SchedulerActivationScheduledEvent e) { }

    /// <summary>Called when a scheduler worker begins draining an activation's mailbox.</summary>
    void OnSchedulerDrainStarted(in SchedulerDrainStartedEvent e) { }

    /// <summary>Called when a drain pass finishes (budget hit, queue empty, or deactivation complete).</summary>
    void OnSchedulerDrainCompleted(in SchedulerDrainCompletedEvent e) { }

    /// <summary>
    ///     Called when a drain pass yields the activation because the drain budget was reached and
    ///     more work remains. The activation is rescheduled at the back of the ready queue.
    /// </summary>
    void OnSchedulerDrainYielded(in SchedulerDrainYieldedEvent e) { }

    /// <summary>
    ///     Called when the scheduler rejects an activation because the bounded ready queue is full
    ///     and the overload mode is <c>RejectWhenFull</c>.
    /// </summary>
    void OnSchedulerOverloadRejected(in SchedulerOverloadRejectedEvent e) { }

    /// <summary>Called at the start of a drain, reporting how long the activation waited in the ready queue.</summary>
    void OnSchedulerActivationWaited(in SchedulerActivationWaitedEvent e) { }

    /// <summary>
    ///     Called once when an activation has been rescheduled repeatedly without a single drain
    ///     pass processing any work — a livelock, distinct from a single stuck work item.
    /// </summary>
    void OnSchedulerDrainStalled(in SchedulerDrainStalledEvent e) { }

    /// <summary>
    ///     Called when the activation scheduler's shutdown has been waiting on its drain workers
    ///     longer than <see cref="DiagnosticOptions.ShutdownStalledThreshold" />.
    /// </summary>
    void OnSchedulerShutdownStalled(in SchedulerShutdownStalledEvent e) { }
}
