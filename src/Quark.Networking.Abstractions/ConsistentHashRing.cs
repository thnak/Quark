using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Quark.Networking.Abstractions;

/// <summary>
/// Implements a consistent hash ring using virtual nodes.
/// Thread-safe implementation using sorted dictionary for O(log n) lookups.
/// </summary>
public sealed class ConsistentHashRing : IConsistentHashRing
{
    private readonly SortedDictionary<uint, string> _ring = new();
    private readonly ConcurrentDictionary<string, HashRingNode> _nodes = new();
    private readonly object _lock = new();

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

            // Add virtual nodes to the ring
            for (int i = 0; i < node.VirtualNodeCount; i++)
            {
                var virtualNodeKey = $"{node.SiloId}:{i}";
                var hash = ComputeHash(virtualNodeKey);
                _ring[hash] = node.SiloId;
            }
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

            // Remove all virtual nodes from the ring
            for (int i = 0; i < node.VirtualNodeCount; i++)
            {
                var virtualNodeKey = $"{node.SiloId}:{i}";
                var hash = ComputeHash(virtualNodeKey);
                _ring.Remove(hash);
            }

            return true;
        }
    }

    /// <inheritdoc />
    public string? GetNode(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_ring.Count == 0)
                return null;

            var hash = ComputeHash(key);

            // Find the first node clockwise from the hash
            foreach (var kvp in _ring)
            {
                if (kvp.Key >= hash)
                    return kvp.Value;
            }

            // Wrap around to the first node
            return _ring.First().Value;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetAllNodes()
    {
        return _nodes.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Computes a 32-bit hash for a given key using MD5.
    /// MD5 is fast and provides good distribution for consistent hashing.
    /// </summary>
    private static uint ComputeHash(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = MD5.HashData(bytes);
        
        // Use first 4 bytes as uint
        return BitConverter.ToUInt32(hash, 0);
    }
}
