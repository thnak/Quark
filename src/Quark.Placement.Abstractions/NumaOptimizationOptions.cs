namespace Quark.Placement.Abstractions;

/// <summary>
/// Configuration options for NUMA-aware actor placement.
/// </summary>
public sealed class NumaOptimizationOptions
{
    /// <summary>
    /// Gets or sets whether NUMA optimization is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the affinity group names that should be co-located on the same NUMA node.
    /// Actors with the same affinity group will be placed together when possible.
    /// </summary>
    public Dictionary<string, List<string>> AffinityGroups { get; set; } = new();

    /// <summary>
    /// Gets or sets the load balancing strategy.
    /// When true, spreads actors evenly across NUMA nodes.
    /// When false, prefers filling one node before using the next.
    /// Default is true (balanced).
    /// </summary>
    public bool BalancedPlacement { get; set; } = true;

    /// <summary>
    /// Gets or sets the threshold for considering a NUMA node as "full" (0-1).
    /// When a node reaches this memory utilization percentage, new actors will be placed on other nodes.
    /// Default is 0.85 (85%).
    /// </summary>
    public double NodeMemoryThreshold { get; set; } = 0.85;

    /// <summary>
    /// Gets or sets the threshold for considering a NUMA node's CPU as "busy" (0-1).
    /// When a node reaches this CPU utilization percentage, new actors will prefer other nodes.
    /// Default is 0.90 (90%).
    /// </summary>
    public double NodeCpuThreshold { get; set; } = 0.90;

    /// <summary>
    /// Gets or sets whether to automatically detect affinity based on actor communication patterns.
    /// When enabled, frequently communicating actors will be co-located.
    /// Default is false.
    /// </summary>
    public bool AutoDetectAffinity { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval for refreshing NUMA node metrics (in seconds).
    /// Default is 5 seconds.
    /// </summary>
    public int MetricsRefreshIntervalSeconds { get; set; } = 5;
}
