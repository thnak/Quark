namespace Quark.Networking.Abstractions;

/// <summary>
/// Strategy for placing actors on silos in the cluster.
/// </summary>
public interface IPlacementPolicy
{
    /// <summary>
    /// Selects a silo for placing an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="availableSilos">The available silos.</param>
    /// <returns>The selected silo ID, or null if no suitable silo found.</returns>
    string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos);
}

/// <summary>
/// Places actors randomly across available silos.
/// Provides good load distribution but no locality.
/// </summary>
public sealed class RandomPlacementPolicy : IPlacementPolicy
{
    private readonly Random _random = new();

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        var index = _random.Next(availableSilos.Count);
        return availableSilos.ElementAt(index);
    }
}

/// <summary>
/// Prefers the local silo if available, otherwise falls back to consistent hashing.
/// Minimizes network hops for local actor access.
/// </summary>
public sealed class LocalPreferredPlacementPolicy : IPlacementPolicy
{
    private readonly string _localSiloId;
    private readonly IConsistentHashRing _hashRing;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalPreferredPlacementPolicy"/> class.
    /// </summary>
    /// <param name="localSiloId">The local silo ID.</param>
    /// <param name="hashRing">The consistent hash ring for fallback.</param>
    public LocalPreferredPlacementPolicy(string localSiloId, IConsistentHashRing hashRing)
    {
        _localSiloId = localSiloId ?? throw new ArgumentNullException(nameof(localSiloId));
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        // Prefer local silo if available
        if (availableSilos.Contains(_localSiloId))
            return _localSiloId;

        // Fall back to consistent hashing
        var key = $"{actorType}:{actorId}";
        return _hashRing.GetNode(key);
    }
}

/// <summary>
/// Placement policy for stateless workers that can run anywhere.
/// Uses round-robin to distribute load evenly.
/// </summary>
public sealed class StatelessWorkerPlacementPolicy : IPlacementPolicy
{
    private int _counter;

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
            return null;

        // Round-robin distribution
        var index = Interlocked.Increment(ref _counter) % availableSilos.Count;
        return availableSilos.ElementAt(index);
    }
}

/// <summary>
/// Uses consistent hashing for deterministic placement.
/// Ensures the same actor always maps to the same silo.
/// </summary>
public sealed class ConsistentHashPlacementPolicy : IPlacementPolicy
{
    private readonly IConsistentHashRing _hashRing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsistentHashPlacementPolicy"/> class.
    /// </summary>
    /// <param name="hashRing">The consistent hash ring.</param>
    public ConsistentHashPlacementPolicy(IConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        var key = $"{actorType}:{actorId}";
        return _hashRing.GetNode(key);
    }
}
