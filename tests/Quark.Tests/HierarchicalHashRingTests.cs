using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
///     Phase 8.3: Tests for hierarchical consistent hash ring with geo-awareness.
/// </summary>
public class HierarchicalHashRingTests
{
    [Fact]
    public void HierarchicalHashRing_AddNode_IncreasesNodeCount()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        var node = new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a");

        // Act
        ring.AddNode(node);

        // Assert
        Assert.Equal(1, ring.TotalNodeCount);
        Assert.Equal(1, ring.RegionCount);
        Assert.Equal(1, ring.ZoneCount);
    }

    [Fact]
    public void HierarchicalHashRing_AddMultipleNodes_TracksCounts()
    {
        // Arrange
        var ring = new HierarchicalHashRing();

        // Act - Add nodes in 2 regions, 3 zones total
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-2", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-3", "us-east", "us-east-1b"));
        ring.AddNode(new HierarchicalHashRingNode("silo-4", "us-west", "us-west-2a"));

        // Assert
        Assert.Equal(4, ring.TotalNodeCount);
        Assert.Equal(2, ring.RegionCount);
        Assert.Equal(3, ring.ZoneCount); // us-east-1a, us-east-1b, us-west-2a (3 unique zones)
    }

    [Fact]
    public void HierarchicalHashRing_RemoveNode_DecreasesNodeCount()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        var node = new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a");
        ring.AddNode(node);

        // Act
        var removed = ring.RemoveNode("silo-1");

        // Assert
        Assert.True(removed);
        Assert.Equal(0, ring.TotalNodeCount);
        Assert.Equal(0, ring.RegionCount);
        Assert.Equal(0, ring.ZoneCount);
    }

    [Fact]
    public void HierarchicalHashRing_GetNode_WithoutPreference_ReturnsNode()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));

        // Act
        var silo = ring.GetNode("actor-123");

        // Assert
        Assert.Equal("silo-1", silo);
    }

    [Fact]
    public void HierarchicalHashRing_GetNode_WithRegionPreference_PrefersRegion()
    {
        // Arrange
        var ring = new HierarchicalHashRing(new GeoRoutingOptions
        {
            PreferSameRegion = true,
            PreferSameZone = false
        });

        // Add nodes in two regions
        ring.AddNode(new HierarchicalHashRingNode("us-east-1", "us-east", "us-east-1a", virtualNodeCount: 100));
        ring.AddNode(new HierarchicalHashRingNode("us-west-1", "us-west", "us-west-2a", virtualNodeCount: 100));

        // Act - Request placement with us-east preference
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var silo = ring.GetNode($"actor-{i}", preferredRegionId: "us-east");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Should heavily prefer us-east region
        Assert.True(placements.ContainsKey("us-east-1"));
        if (placements.ContainsKey("us-east-1"))
        {
            // us-east should get the vast majority (at least 95%)
            Assert.True(placements["us-east-1"] >= 950, 
                $"Expected us-east-1 to get >= 950 actors with region preference, got {placements["us-east-1"]}");
        }
    }

    [Fact]
    public void HierarchicalHashRing_GetNode_WithZonePreference_PrefersZone()
    {
        // Arrange
        var ring = new HierarchicalHashRing(new GeoRoutingOptions
        {
            PreferSameRegion = true,
            PreferSameZone = true
        });

        // Add nodes in same region, different zones
        ring.AddNode(new HierarchicalHashRingNode("silo-1a", "us-east", "us-east-1a", virtualNodeCount: 100));
        ring.AddNode(new HierarchicalHashRingNode("silo-1b", "us-east", "us-east-1b", virtualNodeCount: 100));

        // Act - Request placement with zone preference
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var silo = ring.GetNode($"actor-{i}", preferredRegionId: "us-east", preferredZoneId: "us-east-1a");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Should prefer zone 1a
        Assert.True(placements.ContainsKey("silo-1a"));
        if (placements.ContainsKey("silo-1a"))
        {
            // silo-1a should get the vast majority (at least 95%)
            Assert.True(placements["silo-1a"] >= 950, 
                $"Expected silo-1a to get >= 950 actors with zone preference, got {placements["silo-1a"]}");
        }
    }

    [Fact]
    public void HierarchicalHashRing_GetNode_WithShardGroupPreference_PrefersShardGroup()
    {
        // Arrange
        var ring = new HierarchicalHashRing(new GeoRoutingOptions
        {
            PreferSameShardGroup = true
        });

        // Add nodes in same region, different shard groups
        ring.AddNode(new HierarchicalHashRingNode("silo-s1-1", "us-east", "us-east-1a", "shard-1", virtualNodeCount: 100));
        ring.AddNode(new HierarchicalHashRingNode("silo-s2-1", "us-east", "us-east-1a", "shard-2", virtualNodeCount: 100));

        // Act - Request placement with shard group preference
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var silo = ring.GetNode($"actor-{i}", preferredShardGroupId: "shard-1");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Should prefer shard-1
        Assert.True(placements.ContainsKey("silo-s1-1"));
        if (placements.ContainsKey("silo-s1-1"))
        {
            // silo-s1-1 should get the vast majority (at least 95%)
            Assert.True(placements["silo-s1-1"] >= 950, 
                $"Expected silo-s1-1 to get >= 950 actors with shard preference, got {placements["silo-s1-1"]}");
        }
    }

    [Fact]
    public void HierarchicalHashRing_GetNodesInRegion_ReturnsCorrectSilos()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-2", "us-east", "us-east-1b"));
        ring.AddNode(new HierarchicalHashRingNode("silo-3", "us-west", "us-west-2a"));

        // Act
        var eastSilos = ring.GetNodesInRegion("us-east");

        // Assert
        Assert.Equal(2, eastSilos.Count);
        Assert.Contains("silo-1", eastSilos);
        Assert.Contains("silo-2", eastSilos);
    }

    [Fact]
    public void HierarchicalHashRing_GetNodesInZone_ReturnsCorrectSilos()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-2", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-3", "us-east", "us-east-1b"));

        // Act
        var zoneSilos = ring.GetNodesInZone("us-east", "us-east-1a");

        // Assert
        Assert.Equal(2, zoneSilos.Count);
        Assert.Contains("silo-1", zoneSilos);
        Assert.Contains("silo-2", zoneSilos);
    }

    [Fact]
    public void HierarchicalHashRing_GetNodesInShardGroup_ReturnsCorrectSilos()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a", "shard-1"));
        ring.AddNode(new HierarchicalHashRingNode("silo-2", "us-east", "us-east-1a", "shard-1"));
        ring.AddNode(new HierarchicalHashRingNode("silo-3", "us-east", "us-east-1a", "shard-2"));

        // Act
        var shardSilos = ring.GetNodesInShardGroup("shard-1");

        // Assert
        Assert.Equal(2, shardSilos.Count);
        Assert.Contains("silo-1", shardSilos);
        Assert.Contains("silo-2", shardSilos);
    }

    [Fact]
    public void HierarchicalHashRing_GetRegionForSilo_ReturnsCorrectRegion()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));

        // Act
        var region = ring.GetRegionForSilo("silo-1");

        // Assert
        Assert.Equal("us-east", region);
    }

    [Fact]
    public void HierarchicalHashRing_GetZoneForSilo_ReturnsCorrectZone()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));

        // Act
        var zone = ring.GetZoneForSilo("silo-1");

        // Assert
        Assert.Equal("us-east-1a", zone);
    }

    [Fact]
    public void HierarchicalHashRing_ConsistentPlacement_SameKeyReturnsSameSilo()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("silo-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("silo-2", "us-east", "us-east-1b"));
        ring.AddNode(new HierarchicalHashRingNode("silo-3", "us-west", "us-west-2a"));

        // Act - Get same key multiple times
        var silo1 = ring.GetNode("actor-123");
        var silo2 = ring.GetNode("actor-123");
        var silo3 = ring.GetNode("actor-123");

        // Assert - Should always return the same silo
        Assert.Equal(silo1, silo2);
        Assert.Equal(silo2, silo3);
    }

    [Fact]
    public void HierarchicalHashRing_Distribution_WithMultipleRegions_BalancesLoad()
    {
        // Arrange
        var ring = new HierarchicalHashRing();
        ring.AddNode(new HierarchicalHashRingNode("us-east-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("us-west-1", "us-west", "us-west-2a"));
        ring.AddNode(new HierarchicalHashRingNode("eu-west-1", "eu-west", "eu-west-1a"));

        // Act - Get placement for multiple actors without preference
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 3000; i++)
        {
            var silo = ring.GetNode($"actor-{i}");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Each silo should get roughly 1/3 of actors (allowing for variance)
        Assert.Equal(3, placements.Count);
        foreach (var kvp in placements)
        {
            // Each silo should have between 20% and 55% of actors (allowing for hash variance)
            // Hash distribution can be uneven, especially with only 3 nodes
            Assert.True(kvp.Value >= 600 && kvp.Value <= 1650,
                $"Expected silo {kvp.Key} to get 600-1650 actors, got {kvp.Value}");
        }
    }
}
