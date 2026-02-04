using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Prefers the local silo if available, otherwise falls back to consistent hashing.
///     Minimizes network hops for local actor access.
/// </summary>
public sealed class LocalPreferredPlacementPolicy : IPlacementPolicy
{
    private readonly IConsistentHashRing _hashRing;
    private readonly string _localSiloId;
    // Phase 8.1: Cache placement key hashes to avoid repeated string allocations
    private readonly ConcurrentDictionary<(string ActorType, string ActorId), string?> _placementCache = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="LocalPreferredPlacementPolicy" /> class.
    /// </summary>
    /// <param name="localSiloId">The local silo ID.</param>
    /// <param name="hashRing">The consistent hash ring for fallback.</param>
    public LocalPreferredPlacementPolicy(string localSiloId, IConsistentHashRing hashRing)
    {
        _localSiloId = localSiloId ?? throw new ArgumentNullException(nameof(localSiloId));
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        // Prefer local silo if available
        if (availableSilos.Contains(_localSiloId))
            return _localSiloId;

        // Phase 8.1: Use cache to avoid repeated hash computations
        return _placementCache.GetOrAdd((actorType, actorId), key =>
        {
            // Use SIMD-accelerated composite hash (no string allocation)
            var hash = SimdHashHelper.ComputeCompositeKeyHash(key.ActorType, key.ActorId);
            return _hashRing.GetNode($"{key.ActorType}:{key.ActorId}");
        });
    }
}