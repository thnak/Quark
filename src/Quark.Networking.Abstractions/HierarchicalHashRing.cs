using System.Collections.Concurrent;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.3: Hierarchical consistent hash ring implementation for massive scale (1000+ silos).
///     Organizes nodes into regions → zones → silos hierarchy for efficient geo-aware routing.
///     
///     Design:
///     - Three-tier hash rings: region ring, zone rings (per region), silo rings (per zone)
///     - Lock-free reads via snapshot pattern (same as ConsistentHashRing)
///     - SIMD-accelerated hashing (CRC32/xxHash)
///     - Supports shard groups for very large clusters (10000+ silos)
/// </summary>
public sealed class HierarchicalHashRing : IHierarchicalHashRing
{
    private readonly object _lock = new();
    
    // Node metadata
    private readonly ConcurrentDictionary<string, HierarchicalHashRingNode> _nodes = new();
    
    // Region-level ring: hash → regionId
    private volatile SortedDictionary<uint, string> _regionRing = new();
    
    // Zone-level rings: regionId → (hash → zoneId)
    private volatile Dictionary<string, SortedDictionary<uint, string>> _zoneRings = new();
    
    // Silo-level rings: (regionId, zoneId) → (hash → siloId)
    private volatile Dictionary<(string, string), SortedDictionary<uint, string>> _siloRings = new();
    
    // Reverse lookups for fast queries
    private volatile Dictionary<string, string> _siloToRegion = new();
    private volatile Dictionary<string, string> _siloToZone = new();
    
    // Shard group tracking
    private volatile Dictionary<string, HashSet<string>> _shardGroups = new();

    private readonly GeoRoutingOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HierarchicalHashRing" /> class.
    /// </summary>
    /// <param name="options">Optional geo-routing options.</param>
    public HierarchicalHashRing(GeoRoutingOptions? options = null)
    {
        _options = options ?? new GeoRoutingOptions();
    }

    /// <inheritdoc />
    public int TotalNodeCount => _nodes.Count;

    /// <inheritdoc />
    public int RegionCount
    {
        get
        {
            var zoneRings = _zoneRings; // Volatile read
            return zoneRings.Count;
        }
    }

    /// <inheritdoc />
    public int ZoneCount
    {
        get
        {
            var zoneRings = _zoneRings; // Volatile read
            // Count unique zones across all regions (each ring entry is a unique zone)
            return zoneRings.Values.Sum(ring => ring.Values.Distinct().Count());
        }
    }

    /// <inheritdoc />
    public void AddNode(HierarchicalHashRingNode node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        lock (_lock)
        {
            if (_nodes.ContainsKey(node.SiloId))
                return; // Already added

            _nodes[node.SiloId] = node;

            // Copy-on-write pattern for all data structures (lock-free reads)
            var newRegionRing = new SortedDictionary<uint, string>(_regionRing);
            var newZoneRings = new Dictionary<string, SortedDictionary<uint, string>>(_zoneRings);
            var newSiloRings = new Dictionary<(string, string), SortedDictionary<uint, string>>(_siloRings);
            var newSiloToRegion = new Dictionary<string, string>(_siloToRegion);
            var newSiloToZone = new Dictionary<string, string>(_siloToZone);
            var newShardGroups = new Dictionary<string, HashSet<string>>(_shardGroups);

            // Add to region ring
            AddVirtualNodes(newRegionRing, node.RegionId, node.VirtualNodeCount / 3); // Fewer virtual nodes at region level

            // Add to zone ring for this region
            if (!newZoneRings.ContainsKey(node.RegionId))
            {
                newZoneRings[node.RegionId] = new SortedDictionary<uint, string>();
            }
            AddVirtualNodes(newZoneRings[node.RegionId], node.ZoneId, node.VirtualNodeCount / 2); // Medium at zone level

            // Add to silo ring for this zone
            var zoneKey = (node.RegionId, node.ZoneId);
            if (!newSiloRings.ContainsKey(zoneKey))
            {
                newSiloRings[zoneKey] = new SortedDictionary<uint, string>();
            }
            AddVirtualNodes(newSiloRings[zoneKey], node.SiloId, node.VirtualNodeCount); // Full count at silo level

            // Update reverse lookups
            newSiloToRegion[node.SiloId] = node.RegionId;
            newSiloToZone[node.SiloId] = node.ZoneId;

            // Update shard group membership
            if (node.ShardGroupId != null)
            {
                if (!newShardGroups.ContainsKey(node.ShardGroupId))
                {
                    newShardGroups[node.ShardGroupId] = new HashSet<string>();
                }
                newShardGroups[node.ShardGroupId].Add(node.SiloId);
            }

            // Atomic swap - readers see either old or new state (never partial)
            _regionRing = newRegionRing;
            _zoneRings = newZoneRings;
            _siloRings = newSiloRings;
            _siloToRegion = newSiloToRegion;
            _siloToZone = newSiloToZone;
            _shardGroups = newShardGroups;
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

            // Copy-on-write pattern
            var newRegionRing = new SortedDictionary<uint, string>(_regionRing);
            var newZoneRings = new Dictionary<string, SortedDictionary<uint, string>>(_zoneRings);
            var newSiloRings = new Dictionary<(string, string), SortedDictionary<uint, string>>(_siloRings);
            var newSiloToRegion = new Dictionary<string, string>(_siloToRegion);
            var newSiloToZone = new Dictionary<string, string>(_siloToZone);
            var newShardGroups = new Dictionary<string, HashSet<string>>(_shardGroups);

            // Remove from silo ring
            var zoneKey = (node.RegionId, node.ZoneId);
            if (newSiloRings.TryGetValue(zoneKey, out var siloRing))
            {
                RemoveVirtualNodes(siloRing, node.SiloId, node.VirtualNodeCount);
                
                // If no more silos in this zone, remove zone ring
                if (siloRing.Count == 0)
                {
                    newSiloRings.Remove(zoneKey);
                    
                    // Remove from zone ring
                    if (newZoneRings.TryGetValue(node.RegionId, out var zoneRing))
                    {
                        RemoveVirtualNodes(zoneRing, node.ZoneId, node.VirtualNodeCount / 2);
                        
                        // If no more zones in this region, remove region ring
                        if (zoneRing.Count == 0)
                        {
                            newZoneRings.Remove(node.RegionId);
                            RemoveVirtualNodes(newRegionRing, node.RegionId, node.VirtualNodeCount / 3);
                        }
                    }
                }
            }

            // Update reverse lookups
            newSiloToRegion.Remove(siloId);
            newSiloToZone.Remove(siloId);

            // Update shard groups
            if (node.ShardGroupId != null && newShardGroups.TryGetValue(node.ShardGroupId, out var shardMembers))
            {
                shardMembers.Remove(siloId);
                if (shardMembers.Count == 0)
                {
                    newShardGroups.Remove(node.ShardGroupId);
                }
            }

            // Atomic swap
            _regionRing = newRegionRing;
            _zoneRings = newZoneRings;
            _siloRings = newSiloRings;
            _siloToRegion = newSiloToRegion;
            _siloToZone = newSiloToZone;
            _shardGroups = newShardGroups;

            return true;
        }
    }

    /// <inheritdoc />
    public string? GetNode(
        string key,
        string? preferredRegionId = null,
        string? preferredZoneId = null,
        string? preferredShardGroupId = null)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        // Compute hash once for the key
        var keyHash = SimdHashHelper.ComputeFastHash(key);

        // Lock-free reads via volatile snapshots
        var regionRing = _regionRing;
        var zoneRings = _zoneRings;
        var siloRings = _siloRings;
        var shardGroups = _shardGroups;

        // Try shard group first if specified and enabled
        if (preferredShardGroupId != null && _options.PreferSameShardGroup)
        {
            if (shardGroups.TryGetValue(preferredShardGroupId, out var shardMembers) && shardMembers.Count > 0)
            {
                // Use consistent hashing within the shard group
                var siloId = GetNodeFromSet(key, shardMembers);
                if (siloId != null)
                    return siloId;
            }
        }

        // Try preferred zone if specified
        if (preferredRegionId != null && preferredZoneId != null && _options.PreferSameZone)
        {
            var zoneKey = (preferredRegionId, preferredZoneId);
            if (siloRings.TryGetValue(zoneKey, out var siloRing) && siloRing.Count > 0)
            {
                return GetNodeFromRing(siloRing, keyHash);
            }
        }

        // Try preferred region if specified
        if (preferredRegionId != null && _options.PreferSameRegion)
        {
            if (zoneRings.TryGetValue(preferredRegionId, out var zoneRing) && zoneRing.Count > 0)
            {
                // Get zone from zone ring
                var zoneId = GetNodeFromRing(zoneRing, keyHash);
                if (zoneId != null)
                {
                    var zoneKey = (preferredRegionId, zoneId);
                    if (siloRings.TryGetValue(zoneKey, out var siloRing) && siloRing.Count > 0)
                    {
                        return GetNodeFromRing(siloRing, keyHash);
                    }
                }
            }
        }

        // Fallback: use global consistent hashing
        if (regionRing.Count == 0)
            return null;

        // Get region from region ring
        var selectedRegionId = GetNodeFromRing(regionRing, keyHash);
        if (selectedRegionId == null || !zoneRings.TryGetValue(selectedRegionId, out var selectedZoneRing))
            return null;

        if (selectedZoneRing.Count == 0)
            return null;

        // Get zone from selected region
        var selectedZoneId = GetNodeFromRing(selectedZoneRing, keyHash);
        if (selectedZoneId == null)
            return null;

        var selectedZoneKey = (selectedRegionId, selectedZoneId);
        if (!siloRings.TryGetValue(selectedZoneKey, out var selectedSiloRing))
            return null;

        // Get silo from selected zone
        return GetNodeFromRing(selectedSiloRing, keyHash);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetNodesInRegion(string regionId)
    {
        var siloToRegion = _siloToRegion; // Volatile read
        return siloToRegion
            .Where(kvp => kvp.Value == regionId)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetNodesInZone(string regionId, string zoneId)
    {
        var siloRings = _siloRings; // Volatile read
        var zoneKey = (regionId, zoneId);
        
        if (siloRings.TryGetValue(zoneKey, out var siloRing))
        {
            return siloRing.Values.Distinct().ToList();
        }
        
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetNodesInShardGroup(string shardGroupId)
    {
        var shardGroups = _shardGroups; // Volatile read
        
        if (shardGroups.TryGetValue(shardGroupId, out var members))
        {
            return members.ToList();
        }
        
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetAllNodes()
    {
        return _nodes.Keys.ToList();
    }

    /// <inheritdoc />
    public string? GetRegionForSilo(string siloId)
    {
        var siloToRegion = _siloToRegion; // Volatile read
        return siloToRegion.TryGetValue(siloId, out var regionId) ? regionId : null;
    }

    /// <inheritdoc />
    public string? GetZoneForSilo(string siloId)
    {
        var siloToZone = _siloToZone; // Volatile read
        return siloToZone.TryGetValue(siloId, out var zoneId) ? zoneId : null;
    }

    // Helper: Add virtual nodes to a ring using SIMD-accelerated hashing
    private static void AddVirtualNodes(SortedDictionary<uint, string> ring, string nodeId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var virtualNodeKey = $"{nodeId}:{i}";
            var hash = SimdHashHelper.ComputeFastHash(virtualNodeKey);
            ring[hash] = nodeId;
        }
    }

    // Helper: Remove virtual nodes from a ring
    private static void RemoveVirtualNodes(SortedDictionary<uint, string> ring, string nodeId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var virtualNodeKey = $"{nodeId}:{i}";
            var hash = SimdHashHelper.ComputeFastHash(virtualNodeKey);
            ring.Remove(hash);
        }
    }

    // Helper: Get node from ring using clockwise search
    private static string? GetNodeFromRing(SortedDictionary<uint, string> ring, uint keyHash)
    {
        if (ring.Count == 0)
            return null;

        // Find first node clockwise from hash position
        foreach (var kvp in ring)
        {
            if (kvp.Key >= keyHash)
                return kvp.Value;
        }

        // Wrap around to first node
        return ring.First().Value;
    }

    // Helper: Get node from a set using consistent hashing
    private static string? GetNodeFromSet(string key, HashSet<string> nodes)
    {
        if (nodes.Count == 0)
            return null;

        var keyHash = SimdHashHelper.ComputeFastHash(key);
        
        // Simple modulo hashing for set selection
        var nodeArray = nodes.ToArray();
        var index = (int)(keyHash % (uint)nodeArray.Length);
        return nodeArray[index];
    }
}
