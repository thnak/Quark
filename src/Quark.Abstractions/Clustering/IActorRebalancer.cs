namespace Quark.Abstractions.Clustering;

/// <summary>
/// Provides automatic actor rebalancing across silos based on load and health metrics.
/// </summary>
public interface IActorRebalancer
{
    /// <summary>
    /// Evaluates whether rebalancing is needed and generates migration decisions.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of rebalancing decisions, or empty if no rebalancing is needed.</returns>
    Task<IReadOnlyCollection<RebalancingDecision>> EvaluateRebalancingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a rebalancing decision by migrating an actor to a different silo.
    /// </summary>
    /// <param name="decision">The rebalancing decision to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the migration was successful, false otherwise.</returns>
    Task<bool> ExecuteRebalancingAsync(
        RebalancingDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the migration cost for an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The estimated migration cost (0.0 to 1.0, higher is more expensive).</returns>
    Task<double> CalculateMigrationCostAsync(
        string actorId,
        string actorType,
        CancellationToken cancellationToken = default);
}
