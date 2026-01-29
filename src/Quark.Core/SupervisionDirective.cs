namespace Quark.Core;

/// <summary>
/// Defines the supervision directive to be applied when a child actor fails.
/// These directives determine how a supervisor actor should handle child failures.
/// </summary>
public enum SupervisionDirective
{
    /// <summary>
    /// Resume the child actor, keeping its current state.
    /// Use when the failure is transient and the actor can continue processing.
    /// </summary>
    Resume = 0,

    /// <summary>
    /// Restart the child actor, clearing its state and calling OnActivateAsync.
    /// Use when the actor state may be corrupted and needs to be reinitialized.
    /// </summary>
    Restart = 1,

    /// <summary>
    /// Stop the child actor permanently.
    /// Use when the failure is unrecoverable or the child should no longer exist.
    /// </summary>
    Stop = 2,

    /// <summary>
    /// Escalate the failure to the parent's supervisor.
    /// Use when the supervisor cannot handle the failure and needs parent intervention.
    /// </summary>
    Escalate = 3
}
