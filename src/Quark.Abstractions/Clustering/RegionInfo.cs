namespace Quark.Abstractions.Clustering;

/// <summary>
///     Phase 8.3: Represents a geographical region in a multi-region cluster.
///     Used for geo-aware routing and hierarchical hashing.
/// </summary>
public sealed class RegionInfo
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RegionInfo" /> class.
    /// </summary>
    /// <param name="regionId">The unique identifier for this region (e.g., "us-east-1", "eu-west-1").</param>
    /// <param name="displayName">The human-readable name for this region.</param>
    public RegionInfo(string regionId, string displayName)
    {
        RegionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    ///     Gets the unique identifier for this region.
    /// </summary>
    public string RegionId { get; }

    /// <summary>
    ///     Gets the human-readable name for this region.
    /// </summary>
    public string DisplayName { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RegionInfo other && RegionId == other.RegionId;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return RegionId.GetHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{DisplayName} ({RegionId})";
    }
}
