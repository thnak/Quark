namespace Quark.Core;

/// <summary>
/// Represents the context in which an actor operates.
/// </summary>
public interface IActorContext
{
    /// <summary>
    /// Gets the actor factory for creating and managing actors.
    /// </summary>
    IActorFactory ActorFactory { get; }

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    IServiceProvider? ServiceProvider { get; }
}
