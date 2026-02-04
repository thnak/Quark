namespace Quark.Networking.Abstractions;

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