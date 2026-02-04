namespace Quark.Abstractions.Clustering;

/// <summary>
///     Defines the policy for automatically evicting unhealthy silos from the cluster.
/// </summary>
public enum SiloEvictionPolicy
{
    /// <summary>
    ///     No automatic eviction. Silos must be manually removed.
    /// </summary>
    None,

    /// <summary>
    ///     Evict silos that have not sent a heartbeat within the timeout period.
    /// </summary>
    TimeoutBased,

    /// <summary>
    ///     Evict silos whose health score falls below a threshold.
    /// </summary>
    HealthScoreBased,

    /// <summary>
    ///     Evict silos using both timeout and health score criteria.
    /// </summary>
    Hybrid
}