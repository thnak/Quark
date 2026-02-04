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