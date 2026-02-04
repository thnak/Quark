namespace Quark.Abstractions.Clustering;

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
