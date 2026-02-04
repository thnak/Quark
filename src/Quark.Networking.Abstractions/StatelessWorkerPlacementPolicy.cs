namespace Quark.Networking.Abstractions;

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