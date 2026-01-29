namespace Quark.Abstractions;

/// <summary>
/// Factory interface for creating actor instances.
/// </summary>
public interface IActorFactory
{
    /// <summary>
    /// Creates a new actor instance with the specified ID.
    /// </summary>
    /// <typeparam name="TActor">The type of actor to create.</typeparam>
    /// <param name="actorId">The unique identifier for the actor.</param>
    /// <returns>The created actor instance.</returns>
    TActor CreateActor<TActor>(string actorId) where TActor : IActor;

    /// <summary>
    /// Gets an existing actor or creates a new one if it doesn't exist.
    /// </summary>
    /// <typeparam name="TActor">The type of actor to get or create.</typeparam>
    /// <param name="actorId">The unique identifier for the actor.</param>
    /// <returns>The actor instance.</returns>
    TActor GetOrCreateActor<TActor>(string actorId) where TActor : IActor;
}
