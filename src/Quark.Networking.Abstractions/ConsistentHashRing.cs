using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.1: Optimized consistent hash ring using SIMD-accelerated hashing and lock-free reads.
///     - Uses CRC32/xxHash instead of MD5 (10-100x faster)
///     - Lock-free reads via snapshot pattern (RCU)
///     - Thread-safe writes with minimal lock contention
/// </summary>
public sealed class ConsistentHashRing : IConsistentHashRing
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, HashRingNode> _nodes = new();
    
    // Phase 8.1: Use volatile snapshot for lock-free reads
    private volatile SortedDictionary<uint, string> _ring = new();

    /// <inheritdoc />
    public int NodeCount => _nodes.Count;

    /// <inheritdoc />
    public void AddNode(HashRingNode node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        lock (_lock)
        {
            if (_nodes.ContainsKey(node.SiloId))
                return; // Already added

            _nodes[node.SiloId] = node;

            // Phase 8.1: Copy-on-write pattern for lock-free reads
            var newRing = new SortedDictionary<uint, string>(_ring);

            // Add virtual nodes to the ring with SIMD-accelerated hash
            for (var i = 0; i < node.VirtualNodeCount; i++)
            {
                var virtualNodeKey = $"{node.SiloId}:{i}";
                var hash = SimdHashHelper.ComputeFastHash(virtualNodeKey);
                newRing[hash] = node.SiloId;
            }

            // Atomic swap - readers see either old or new ring (never partial state)
            _ring = newRing;
        }
    }

    /// <inheritdoc />
    public bool RemoveNode(string siloId)
    {
        if (string.IsNullOrEmpty(siloId))
            throw new ArgumentNullException(nameof(siloId));

        lock (_lock)
        {
            if (!_nodes.TryRemove(siloId, out var node))
                return false;

            // Phase 8.1: Copy-on-write pattern for lock-free reads
            var newRing = new SortedDictionary<uint, string>(_ring);

            // Remove all virtual nodes from the ring with SIMD-accelerated hash
            for (var i = 0; i < node.VirtualNodeCount; i++)
            {
                var virtualNodeKey = $"{node.SiloId}:{i}";
                var hash = SimdHashHelper.ComputeFastHash(virtualNodeKey);
                newRing.Remove(hash);
            }

            // Atomic swap
            _ring = newRing;
            return true;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? GetNode(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        // Phase 8.1: Lock-free read via volatile snapshot
        var currentRing = _ring;
        
        if (currentRing.Count == 0)
            return null;

        // Phase 8.1: Use SIMD-accelerated hash (10-100x faster than MD5)
        var hash = SimdHashHelper.ComputeFastHash(key);

        // Find the first node clockwise from the hash
        foreach (var kvp in currentRing)
            if (kvp.Key >= hash)
                return kvp.Value;

        // Wrap around to the first node
        return currentRing.First().Value;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetAllNodes()
    {
        return _nodes.Keys.ToList().AsReadOnly();
    }


}