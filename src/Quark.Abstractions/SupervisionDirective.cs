namespace Quark.Abstractions;

/// <summary>
/// Defines the supervision directive to be applied when a child actor fails.
/// </summary>
public enum SupervisionDirective : byte
{
    /// <summary>
    /// Resume the child actor, keeping its current state.
    /// </summary>
    Resume = 0,

    /// <summary>
    /// Restart the child actor, clearing its state.
    /// </summary>
    Restart = 1,

    /// <summary>
    /// Stop the child actor permanently.
    /// </summary>
    Stop = 2,

    /// <summary>
    /// Escalate the failure to the parent's supervisor.
    /// </summary>
    Escalate = 3
}
