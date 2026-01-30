namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.3: Represents a node in the hierarchical consistent hash ring with geo-awareness.
///     Extends the basic HashRingNode with region, zone, and shard group information.
/// </summary>
public sealed class HierarchicalHashRingNode
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HierarchicalHashRingNode" /> class.
    /// </summary>
    /// <param name="siloId">The unique identifier for this silo.</param>
    /// <param name="regionId">The region this silo belongs to.</param>
    /// <param name="zoneId">The availability zone this silo belongs to.</param>
    /// <param name="shardGroupId">Optional shard group identifier for very large clusters.</param>
    /// <param name="virtualNodeCount">Number of virtual nodes (default: 150).</param>
    public HierarchicalHashRingNode(
        string siloId,
        string regionId,
        string zoneId,
        string? shardGroupId = null,
        int virtualNodeCount = 150)
    {
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        RegionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        ZoneId = zoneId ?? throw new ArgumentNullException(nameof(zoneId));
        ShardGroupId = shardGroupId;
        VirtualNodeCount = virtualNodeCount;
    }

    /// <summary>
    ///     Gets the silo ID.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    ///     Gets the region ID.
    /// </summary>
    public string RegionId { get; }

    /// <summary>
    ///     Gets the zone ID.
    /// </summary>
    public string ZoneId { get; }

    /// <summary>
    ///     Gets the optional shard group ID.
    /// </summary>
    public string? ShardGroupId { get; }

    /// <summary>
    ///     Gets the number of virtual nodes for this physical node.
    /// </summary>
    public int VirtualNodeCount { get; }
}

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

/// <summary>
///     Phase 8.3: Fallback strategy for geo-aware routing when preferred location is unavailable.
/// </summary>
public enum GeoFallbackStrategy
{
    /// <summary>
    ///     Fall back to any available silo (no geographic preference).
    /// </summary>
    Any,

    /// <summary>
    ///     Fall back to the nearest region based on configured latency matrix.
    /// </summary>
    NearestRegion,

    /// <summary>
    ///     Fail the request if no silos available in preferred region.
    /// </summary>
    Fail
}

/// <summary>
///     Phase 8.3: Hierarchical consistent hash ring for massive scale (1000+ silos).
///     Supports multi-region, multi-zone, and shard group organization.
/// </summary>
public interface IHierarchicalHashRing
{
    /// <summary>
    ///     Gets the total number of physical nodes across all regions/zones.
    /// </summary>
    int TotalNodeCount { get; }

    /// <summary>
    ///     Gets the number of regions in the ring.
    /// </summary>
    int RegionCount { get; }

    /// <summary>
    ///     Gets the number of zones across all regions.
    /// </summary>
    int ZoneCount { get; }

    /// <summary>
    ///     Adds a node to the hierarchical hash ring.
    /// </summary>
    /// <param name="node">The node to add with region/zone/shard information.</param>
    void AddNode(HierarchicalHashRingNode node);

    /// <summary>
    ///     Removes a node from the hash ring.
    /// </summary>
    /// <param name="siloId">The silo ID to remove.</param>
    /// <returns>True if the node was removed, false if it wasn't found.</returns>
    bool RemoveNode(string siloId);

    /// <summary>
    ///     Gets the node responsible for a given key with geo-aware routing.
    /// </summary>
    /// <param name="key">The key (typically actor ID + actor type).</param>
    /// <param name="preferredRegionId">Optional preferred region for placement.</param>
    /// <param name="preferredZoneId">Optional preferred zone for placement.</param>
    /// <param name="preferredShardGroupId">Optional preferred shard group.</param>
    /// <returns>The silo ID responsible for this key, or null if no nodes exist.</returns>
    string? GetNode(
        string key,
        string? preferredRegionId = null,
        string? preferredZoneId = null,
        string? preferredShardGroupId = null);

    /// <summary>
    ///     Gets all nodes in a specific region.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <returns>Collection of silo IDs in the specified region.</returns>
    IReadOnlyCollection<string> GetNodesInRegion(string regionId);

    /// <summary>
    ///     Gets all nodes in a specific zone.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <param name="zoneId">The zone ID.</param>
    /// <returns>Collection of silo IDs in the specified zone.</returns>
    IReadOnlyCollection<string> GetNodesInZone(string regionId, string zoneId);

    /// <summary>
    ///     Gets all nodes in a specific shard group.
    /// </summary>
    /// <param name="shardGroupId">The shard group ID.</param>
    /// <returns>Collection of silo IDs in the specified shard group.</returns>
    IReadOnlyCollection<string> GetNodesInShardGroup(string shardGroupId);

    /// <summary>
    ///     Gets all nodes in the ring.
    /// </summary>
    /// <returns>Collection of all silo IDs.</returns>
    IReadOnlyCollection<string> GetAllNodes();

    /// <summary>
    ///     Gets the region ID for a given silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <returns>The region ID, or null if silo not found.</returns>
    string? GetRegionForSilo(string siloId);

    /// <summary>
    ///     Gets the zone ID for a given silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <returns>The zone ID, or null if silo not found.</returns>
    string? GetZoneForSilo(string siloId);
}
