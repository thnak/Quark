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