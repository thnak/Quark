namespace Quark.Abstractions.Clustering;

/// <summary>
/// Represents the result of a routing decision.
/// </summary>
public enum RoutingResult
{
    /// <summary>
    /// Actor is located on a remote silo, requires network call.
    /// </summary>
    Remote,

    /// <summary>
    /// Actor is on the same silo, can be invoked locally.
    /// </summary>
    LocalSilo,

    /// <summary>
    /// Actor is in the same process, can be invoked directly.
    /// </summary>
    SameProcess,

    /// <summary>
    /// Routing target not found or unavailable.
    /// </summary>
    NotFound
}

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

/// <summary>
/// Provides intelligent routing for actor invocations with local bypass optimization.
/// </summary>
public interface ISmartRouter
{
    /// <summary>
    /// Determines the optimal route for an actor invocation.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A routing decision indicating how to reach the actor.</returns>
    Task<RoutingDecision> RouteAsync(
        string actorId,
        string actorType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached routing information for an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    void InvalidateCache(string actorId, string actorType);

    /// <summary>
    /// Gets routing statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>A dictionary of routing statistics.</returns>
    IReadOnlyDictionary<string, long> GetRoutingStatistics();
}
