namespace Quark.Abstractions.Clustering;

/// <summary>
///     Represents the location of an actor in the cluster.
/// </summary>
public sealed class ActorLocation
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorLocation" /> class.
    /// </summary>
    public ActorLocation(string actorId, string actorType, string siloId)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    ///     Gets the actor type name.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    ///     Gets the silo ID where the actor is located.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    ///     Gets the timestamp when this location was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; internal set; }
}