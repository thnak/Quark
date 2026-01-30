namespace Quark.Abstractions.Clustering;

/// <summary>
/// Configuration options for smart routing.
/// </summary>
public sealed class SmartRoutingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether smart routing is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether local bypass optimization is enabled.
    /// When true, actors on the same silo are invoked directly without network overhead.
    /// Default is true.
    /// </summary>
    public bool EnableLocalBypass { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether same-process optimization is enabled.
    /// When true, actors in the same process are invoked directly.
    /// Default is true.
    /// </summary>
    public bool EnableSameProcessOptimization { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of entries in the routing cache.
    /// Default is 10000.
    /// </summary>
    public int CacheSize { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the time-to-live for cached routing entries.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a value indicating whether to collect routing statistics.
    /// Default is true.
    /// </summary>
    public bool EnableStatistics { get; set; } = true;
}
