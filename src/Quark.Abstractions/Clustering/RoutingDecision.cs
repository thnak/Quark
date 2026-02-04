namespace Quark.Abstractions.Clustering;

/// <summary>
/// Represents a routing decision for an actor invocation.
/// </summary>
public sealed class RoutingDecision
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingDecision"/> class.
    /// </summary>
    public RoutingDecision(
        string actorId,
        string actorType,
        RoutingResult result,
        string? targetSiloId = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        Result = result;
        TargetSiloId = targetSiloId;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the routing result.
    /// </summary>
    public RoutingResult Result { get; }

    /// <summary>
    /// Gets the target silo ID (null for SameProcess routing).
    /// </summary>
    public string? TargetSiloId { get; }

    /// <summary>
    /// Gets the timestamp when this decision was made.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}