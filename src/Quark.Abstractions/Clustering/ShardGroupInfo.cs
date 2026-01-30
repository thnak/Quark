namespace Quark.Abstractions.Clustering;

/// <summary>
///     Phase 8.3: Represents a shard group - a logical grouping of silos for coordinated operations.
///     Used in very large clusters (1000+ silos) to organize silos into manageable groups.
/// </summary>
public sealed class ShardGroupInfo
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ShardGroupInfo" /> class.
    /// </summary>
    /// <param name="shardGroupId">The unique identifier for this shard group.</param>
    /// <param name="regionId">The region this shard group belongs to.</param>
    /// <param name="zoneId">Optional zone identifier if shard is zone-specific.</param>
    public ShardGroupInfo(string shardGroupId, string regionId, string? zoneId = null)
    {
        ShardGroupId = shardGroupId ?? throw new ArgumentNullException(nameof(shardGroupId));
        RegionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        ZoneId = zoneId;
    }

    /// <summary>
    ///     Gets the unique identifier for this shard group.
    /// </summary>
    public string ShardGroupId { get; }

    /// <summary>
    ///     Gets the region this shard group belongs to.
    /// </summary>
    public string RegionId { get; }

    /// <summary>
    ///     Gets the optional zone identifier if shard is zone-specific.
    /// </summary>
    public string? ZoneId { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ShardGroupInfo other && ShardGroupId == other.ShardGroupId;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return ShardGroupId.GetHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ZoneId != null
            ? $"Shard {ShardGroupId} in {ZoneId}, {RegionId}"
            : $"Shard {ShardGroupId} in {RegionId}";
    }
}
