using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Uses consistent hashing for deterministic placement.
///     Ensures the same actor always maps to the same silo.
/// </summary>
public sealed class ConsistentHashPlacementPolicy : IPlacementPolicy
{
    private readonly IConsistentHashRing _hashRing;
    // Phase 8.1: Cache placement decisions to avoid repeated hash computations
    private readonly ConcurrentDictionary<(string ActorType, string ActorId), string?> _placementCache = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConsistentHashPlacementPolicy" /> class.
    /// </summary>
    /// <param name="hashRing">The consistent hash ring.</param>
    public ConsistentHashPlacementPolicy(IConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        // Phase 8.1: Use cache to avoid repeated hash computations and string allocations
        return _placementCache.GetOrAdd((actorType, actorId), key =>
        {
            // Use SIMD-accelerated composite hash (no string allocation)
            var hash = SimdHashHelper.ComputeCompositeKeyHash(key.ActorType, key.ActorId);
            return _hashRing.GetNode($"{key.ActorType}:{key.ActorId}");
        });
    }
}