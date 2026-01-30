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

/// <summary>
///     Configuration options for silo eviction policies.
/// </summary>
public sealed class EvictionPolicyOptions
{
    /// <summary>
    ///     Gets or sets the eviction policy to use.
    ///     Default is <see cref="SiloEvictionPolicy.TimeoutBased" />.
    /// </summary>
    public SiloEvictionPolicy Policy { get; set; } = SiloEvictionPolicy.TimeoutBased;

    /// <summary>
    ///     Gets or sets the heartbeat timeout in seconds.
    ///     Silos that have not sent a heartbeat within this period will be evicted.
    ///     Default is 30 seconds.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Gets or sets the minimum health score threshold (0-100).
    ///     Silos with a health score below this threshold will be evicted.
    ///     Default is 30.0.
    /// </summary>
    public double HealthScoreThreshold { get; set; } = 30.0;

    /// <summary>
    ///     Gets or sets the number of consecutive unhealthy checks before eviction.
    ///     This prevents temporary issues from triggering eviction.
    ///     Default is 3.
    /// </summary>
    public int ConsecutiveUnhealthyChecks { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the interval between health checks in seconds.
    ///     Default is 10 seconds.
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 10;

    /// <summary>
    ///     Gets or sets whether to enable split-brain detection.
    ///     Default is true.
    /// </summary>
    public bool EnableSplitBrainDetection { get; set; } = true;

    /// <summary>
    ///     Gets or sets the minimum cluster size for quorum-based decisions.
    ///     Default is 3.
    /// </summary>
    public int MinimumClusterSizeForQuorum { get; set; } = 3;

    /// <summary>
    ///     Gets or sets whether to enable automatic cluster rebalancing after eviction.
    ///     Default is true.
    /// </summary>
    public bool EnableAutomaticRebalancing { get; set; } = true;
}
