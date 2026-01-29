namespace Quark.Core;

/// <summary>
/// Provides factory methods for creating actor instances.
/// </summary>
public interface IActorFactory
{
    /// <summary>
    /// Creates a new actor instance of the specified type.
    /// </summary>
    /// <typeparam name="TActor">The type of actor to create.</typeparam>
    /// <param name="actorId">The unique identifier for the actor.</param>
    /// <returns>A new actor instance.</returns>
    TActor CreateActor<TActor>(string actorId) where TActor : IActor;

    /// <summary>
    /// Gets an existing actor instance or creates a new one if it doesn't exist.
    /// </summary>
    /// <typeparam name="TActor">The type of actor to get or create.</typeparam>
    /// <param name="actorId">The unique identifier for the actor.</param>
    /// <returns>An actor instance.</returns>
    TActor GetOrCreateActor<TActor>(string actorId) where TActor : IActor;
}
