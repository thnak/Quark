namespace Quark.Core;

/// <summary>
/// Base interface for all actors in the Quark framework.
/// Actors are lightweight, stateful objects that process messages sequentially.
/// </summary>
public interface IActor
{
    /// <summary>
    /// Gets the unique identifier for this actor instance.
    /// </summary>
    string ActorId { get; }
}
