using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.3: Geo-aware placement policy using hierarchical consistent hashing.
///     Supports multi-region, multi-zone, and shard group placement for massive scale (1000+ silos).
/// </summary>
public sealed class GeoAwarePlacementPolicy : IPlacementPolicy
{
    private readonly IHierarchicalHashRing _hierarchicalRing;
    private readonly string? _preferredRegionId;
    private readonly string? _preferredZoneId;
    private readonly string? _preferredShardGroupId;
    // Cache placement decisions to avoid repeated hash computations
    private readonly ConcurrentDictionary<(string ActorType, string ActorId), string?> _placementCache = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="GeoAwarePlacementPolicy" /> class.
    /// </summary>
    /// <param name="hierarchicalRing">The hierarchical hash ring.</param>
    /// <param name="preferredRegionId">Optional preferred region for placement.</param>
    /// <param name="preferredZoneId">Optional preferred zone for placement.</param>
    /// <param name="preferredShardGroupId">Optional preferred shard group for placement.</param>
    public GeoAwarePlacementPolicy(
        IHierarchicalHashRing hierarchicalRing,
        string? preferredRegionId = null,
        string? preferredZoneId = null,
        string? preferredShardGroupId = null)
    {
        _hierarchicalRing = hierarchicalRing ?? throw new ArgumentNullException(nameof(hierarchicalRing));
        _preferredRegionId = preferredRegionId;
        _preferredZoneId = preferredZoneId;
        _preferredShardGroupId = preferredShardGroupId;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        // Use cache to avoid repeated hash computations
        return _placementCache.GetOrAdd((actorType, actorId), key =>
        {
            // Use hierarchical ring with geo preferences
            return _hierarchicalRing.GetNode(
                $"{key.ActorType}:{key.ActorId}",
                _preferredRegionId,
                _preferredZoneId,
                _preferredShardGroupId);
        });
    }
}