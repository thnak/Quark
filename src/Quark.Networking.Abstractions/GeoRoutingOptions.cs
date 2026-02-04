namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.3: Options for hierarchical hash ring geo-aware routing.
/// </summary>
public sealed class GeoRoutingOptions
{
    /// <summary>
    ///     Gets or sets whether to prefer silos in the same region for actor placement.
    ///     Default is true.
    /// </summary>
    public bool PreferSameRegion { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to prefer silos in the same zone within a region.
    ///     Default is true.
    /// </summary>
    public bool PreferSameZone { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to prefer silos in the same shard group.
    ///     Only applies to very large clusters using shard groups.
    ///     Default is false.
    /// </summary>
    public bool PreferSameShardGroup { get; set; } = false;

    /// <summary>
    ///     Gets or sets the fallback strategy when no nodes are available in preferred region/zone.
    /// </summary>
    public GeoFallbackStrategy FallbackStrategy { get; set; } = GeoFallbackStrategy.NearestRegion;
}