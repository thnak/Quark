namespace Quark.Placement.Locality;

/// <summary>
/// Represents a pair of actors that communicate frequently.
/// </summary>
public sealed class ActorPair
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorPair"/> class.
    /// </summary>
    public ActorPair(string fromActorId, string toActorId, CommunicationMetrics metrics)
    {
        FromActorId = fromActorId ?? throw new ArgumentNullException(nameof(fromActorId));
        ToActorId = toActorId ?? throw new ArgumentNullException(nameof(toActorId));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Gets the source actor ID.
    /// </summary>
    public string FromActorId { get; }

    /// <summary>
    /// Gets the destination actor ID.
    /// </summary>
    public string ToActorId { get; }

    /// <summary>
    /// Gets the communication metrics between the actors.
    /// </summary>
    public CommunicationMetrics Metrics { get; }
}