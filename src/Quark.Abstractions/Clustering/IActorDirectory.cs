namespace Quark.Abstractions.Clustering;

/// <summary>
///     Provides actor location management for distributed actor routing.
/// </summary>
public interface IActorDirectory
{
    /// <summary>
    ///     Registers an actor's location in the directory.
    /// </summary>
    /// <param name="location">The actor location.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RegisterActorAsync(ActorLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Unregisters an actor from the directory.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UnregisterActorAsync(string actorId, string actorType, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Looks up the location of an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The actor location, or null if not found.</returns>
    Task<ActorLocation?> LookupActorAsync(string actorId, string actorType,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all actors on a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of actor locations.</returns>
    Task<IReadOnlyCollection<ActorLocation>> GetActorsBySiloAsync(string siloId,
        CancellationToken cancellationToken = default);
}