using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Strategy for placing actors on silos in the cluster.
/// </summary>
public interface IPlacementPolicy
{
    /// <summary>
    ///     Selects a silo for placing an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="availableSilos">The available silos.</param>
    /// <returns>The selected silo ID, or null if no suitable silo found.</returns>
    string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos);
}

/// <summary>
///     Places actors randomly across available silos.
///     Provides good load distribution but no locality.
/// </summary>
public sealed class RandomPlacementPolicy : IPlacementPolicy
{
    private readonly Random _random = new();
    // Phase 8.1: Cache to avoid repeated ElementAt() calls (O(n) on ReadOnlyCollection)
    // Using object to avoid volatile tuple restriction
    private object? _cachedSilosLock = new();
    private (IReadOnlyCollection<string> Collection, string[] Array)? _cachedSilos;

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        // Phase 8.1: Convert to array once for O(1) indexing with thread-safe caching
        var cached = _cachedSilos;
        string[] siloArray;
        
        if (cached == null || !ReferenceEquals(cached.Value.Collection, availableSilos))
        {
            lock (_cachedSilosLock!)
            {
                cached = _cachedSilos;
                if (cached == null || !ReferenceEquals(cached.Value.Collection, availableSilos))
                {
                    siloArray = availableSilos.ToArray();
                    _cachedSilos = (availableSilos, siloArray);
                }
                else
                {
                    siloArray = cached.Value.Array;
                }
            }
        }
        else
        {
            siloArray = cached.Value.Array;
        }

        var index = _random.Next(siloArray.Length);
        return siloArray[index];
    }
}

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

/// <summary>
///     Placement policy for stateless workers that can run anywhere.
///     Uses round-robin to distribute load evenly.
/// </summary>
public sealed class StatelessWorkerPlacementPolicy : IPlacementPolicy
{
    private int _counter;
    // Phase 8.1: Cache to avoid repeated ElementAt() calls
    private object? _cachedSilosLock = new();
    private (IReadOnlyCollection<string> Collection, string[] Array)? _cachedSilos;

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        // Phase 8.1: Convert to array once for O(1) indexing with thread-safe caching
        var cached = _cachedSilos;
        string[] siloArray;
        
        if (cached == null || !ReferenceEquals(cached.Value.Collection, availableSilos))
        {
            lock (_cachedSilosLock!)
            {
                cached = _cachedSilos;
                if (cached == null || !ReferenceEquals(cached.Value.Collection, availableSilos))
                {
                    siloArray = availableSilos.ToArray();
                    _cachedSilos = (availableSilos, siloArray);
                }
                else
                {
                    siloArray = cached.Value.Array;
                }
            }
        }
        else
        {
            siloArray = cached.Value.Array;
        }

        // Phase 8.1: Use modulo directly instead of increment then modulo
        var nextIndex = Interlocked.Increment(ref _counter);
        var index = (int)((uint)nextIndex % (uint)siloArray.Length);
        return siloArray[index];
    }
}

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