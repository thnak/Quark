namespace Quark.Abstractions.Clustering;

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