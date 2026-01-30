namespace Quark.Abstractions.Clustering;

/// <summary>
///     Phase 8.3: Represents an availability zone within a region.
///     Used for zone-aware placement and fault isolation.
/// </summary>
public sealed class ZoneInfo
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ZoneInfo" /> class.
    /// </summary>
    /// <param name="zoneId">The unique identifier for this zone (e.g., "us-east-1a", "eu-west-1b").</param>
    /// <param name="regionId">The region this zone belongs to.</param>
    /// <param name="displayName">The human-readable name for this zone.</param>
    public ZoneInfo(string zoneId, string regionId, string displayName)
    {
        ZoneId = zoneId ?? throw new ArgumentNullException(nameof(zoneId));
        RegionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    ///     Gets the unique identifier for this zone.
    /// </summary>
    public string ZoneId { get; }

    /// <summary>
    ///     Gets the region this zone belongs to.
    /// </summary>
    public string RegionId { get; }

    /// <summary>
    ///     Gets the human-readable name for this zone.
    /// </summary>
    public string DisplayName { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ZoneInfo other && ZoneId == other.ZoneId;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return ZoneId.GetHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{DisplayName} ({ZoneId}) in {RegionId}";
    }
}
