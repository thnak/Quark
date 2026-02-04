namespace Quark.Abstractions;

/// <summary>
///     Strategy for restarting failed child actors.
/// </summary>
public enum RestartStrategy
{
    /// <summary>
    ///     Restart only the failed child actor.
    ///     Other siblings continue running.
    /// </summary>
    OneForOne,

    /// <summary>
    ///     Restart all child actors when one fails.
    ///     Ensures consistent state across siblings.
    /// </summary>
    AllForOne,

    /// <summary>
    ///     Restart the failed child and all siblings created after it.
    ///     Maintains temporal ordering of actor creation.
    /// </summary>
    RestForOne
}