namespace Quark.Abstractions.Clustering;

/// <summary>
/// Represents the location of an actor in the cluster.
/// </summary>
public sealed class ActorLocation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorLocation"/> class.
    /// </summary>
    public ActorLocation(string actorId, string actorType, string siloId)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type name.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the silo ID where the actor is located.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    /// Gets the timestamp when this location was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; internal set; }
}

/// <summary>
/// Provides actor location management for distributed actor routing.
/// </summary>
public interface IActorDirectory
{
    /// <summary>
    /// Registers an actor's location in the directory.
    /// </summary>
    /// <param name="location">The actor location.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RegisterActorAsync(ActorLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters an actor from the directory.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UnregisterActorAsync(string actorId, string actorType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the location of an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The actor location, or null if not found.</returns>
    Task<ActorLocation?> LookupActorAsync(string actorId, string actorType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all actors on a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of actor locations.</returns>
    Task<IReadOnlyCollection<ActorLocation>> GetActorsBySiloAsync(string siloId, CancellationToken cancellationToken = default);
}
