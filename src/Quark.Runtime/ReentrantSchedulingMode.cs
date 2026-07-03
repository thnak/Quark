namespace Quark.Runtime;

/// <summary>
///     Scheduler policy that controls how <see cref="GrainActivation.PostAsync"/> handles work items
///     when the associated behavior is marked <c>[Reentrant]</c>.
/// </summary>
/// <remarks>
///     This is an internal scheduler-policy surface. The user-facing knob remains the
///     <c>[Reentrant]</c> attribute on the behavior class.
/// </remarks>
internal enum ReentrantSchedulingMode
{
    /// <summary>
    ///     Non-reentrant (default). Each work item is queued in the mailbox channel and drained by the
    ///     <see cref="IActivationScheduler"/>; at most one turn executes at a time.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Compatibility mode for <c>[Reentrant]</c> behaviors — preserves pre-Phase-5 semantics
    ///     exactly. Work items bypass the mailbox channel, the scheduler ready queue, and all
    ///     QoS/fairness machinery: each item is executed inline on the caller's execution context
    ///     with no coordination with other concurrent callers.
    ///     <para>
    ///         <b>Scheduler-invisible:</b> reentrant work is not counted against the concurrency cap,
    ///         does not consume a drain budget slot, does not fire scheduler diagnostics
    ///         (<see cref="IQuarkDiagnosticListener.OnSchedulerDrainStarted"/> etc.), and cannot be
    ///         rejected by the ready-queue overload policy. All Phase-4 QoS machinery is silently
    ///         skipped. This is a known, accepted limitation of the compatibility mode; it exists so
    ///         that v1 scheduler work does not alter the observable behavior of existing
    ///         <c>[Reentrant]</c> grains.
    ///     </para>
    ///     <para>
    ///         <b>Lifecycle barrier:</b> deactivation initiated by
    ///         <see cref="GrainActivation.Deactivate"/> always uses the scheduler drain path
    ///         (it sets <c>Deactivating</c> status and calls
    ///         <see cref="IActivationScheduler.ScheduleAsync"/>) and is therefore never routed
    ///         through this mode. Deactivation initiated by
    ///         <see cref="IAsyncDisposable.DisposeAsync"/> posts the deactivation work item through
    ///         <see cref="GrainActivation.PostAsync"/> and therefore does run immediately for
    ///         reentrant activations, but it is a single serialized call from <c>DisposeAsync</c>
    ///         itself, not a concurrent one.
    ///     </para>
    /// </summary>
    Immediate = 1,
}
