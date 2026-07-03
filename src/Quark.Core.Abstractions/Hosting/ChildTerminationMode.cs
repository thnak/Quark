namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Controls what happens to a child grain when the parent terminates intentionally.
/// </summary>
public enum ChildTerminationMode
{
    /// <summary>Terminate this child when the parent terminates intentionally (default).</summary>
    Cascade = 0,

    /// <summary>Track the link but never cascade — explicit opt-out for this child.</summary>
    Orphan = 1,
}
