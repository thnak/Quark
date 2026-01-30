namespace Quark.Abstractions.Clustering;

/// <summary>
/// Represents the reason for actor rebalancing.
/// </summary>
public enum RebalancingReason
{
    /// <summary>
    /// Rebalancing due to load imbalance across silos.
    /// </summary>
    LoadImbalance,

    /// <summary>
    /// Rebalancing due to silo health degradation.
    /// </summary>
    HealthDegradation,

    /// <summary>
    /// Rebalancing due to new silo joining the cluster.
    /// </summary>
    SiloJoined,

    /// <summary>
    /// Rebalancing due to silo leaving the cluster.
    /// </summary>
    SiloLeft,

    /// <summary>
    /// Manual rebalancing triggered by operator.
    /// </summary>
    Manual
}

/// <summary>
/// Represents a decision to migrate an actor to a different silo.
/// </summary>
public sealed class RebalancingDecision
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RebalancingDecision"/> class.
    /// </summary>
    public RebalancingDecision(
        string actorId,
        string actorType,
        string sourceSiloId,
        string targetSiloId,
        RebalancingReason reason,
        double migrationCost)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        SourceSiloId = sourceSiloId ?? throw new ArgumentNullException(nameof(sourceSiloId));
        TargetSiloId = targetSiloId ?? throw new ArgumentNullException(nameof(targetSiloId));
        Reason = reason;
        MigrationCost = migrationCost;
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
    /// Gets the source silo ID.
    /// </summary>
    public string SourceSiloId { get; }

    /// <summary>
    /// Gets the target silo ID.
    /// </summary>
    public string TargetSiloId { get; }

    /// <summary>
    /// Gets the reason for rebalancing.
    /// </summary>
    public RebalancingReason Reason { get; }

    /// <summary>
    /// Gets the estimated migration cost (0.0 to 1.0, higher is more expensive).
    /// </summary>
    public double MigrationCost { get; }

    /// <summary>
    /// Gets the timestamp when this decision was made.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

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
