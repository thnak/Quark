using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

public class ConsistentHashRingTests
{
    [Fact]
    public void ConsistentHashRing_AddNode_IncreasesNodeCount()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        var node = new HashRingNode("silo-1");

        // Act
        ring.AddNode(node);

        // Assert
        Assert.Equal(1, ring.NodeCount);
    }

    [Fact]
    public void ConsistentHashRing_RemoveNode_DecreasesNodeCount()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        var node = new HashRingNode("silo-1");
        ring.AddNode(node);

        // Act
        var removed = ring.RemoveNode("silo-1");

        // Assert
        Assert.True(removed);
        Assert.Equal(0, ring.NodeCount);
    }

    [Fact]
    public void ConsistentHashRing_GetNode_ReturnsCorrectSilo()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1"));

        // Act
        var silo = ring.GetNode("actor-123");

        // Assert
        Assert.Equal("silo-1", silo);
    }

    [Fact]
    public void ConsistentHashRing_GetNode_WithMultipleNodes_DistributesActors()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1"));
        ring.AddNode(new HashRingNode("silo-2"));
        ring.AddNode(new HashRingNode("silo-3"));

        // Act - Get placement for multiple actors
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var silo = ring.GetNode($"actor-{i}");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Each silo should get some actors (rough distribution check)
        Assert.Equal(3, placements.Count);
        foreach (var count in placements.Values)
        {
            // Each silo should have at least 20% of actors (allowing for variance)
            Assert.True(count > 200, $"Expected > 200 actors, got {count}");
        }
    }

    [Fact]
    public void ConsistentHashRing_GetNode_WithNoNodes_ReturnsNull()
    {
        // Arrange
        var ring = new ConsistentHashRing();

        // Act
        var silo = ring.GetNode("actor-123");

        // Assert
        Assert.Null(silo);
    }

    [Fact]
    public void ConsistentHashRing_GetNode_ConsistentForSameKey()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1"));
        ring.AddNode(new HashRingNode("silo-2"));

        // Act
        var silo1 = ring.GetNode("actor-123");
        var silo2 = ring.GetNode("actor-123");

        // Assert
        Assert.Equal(silo1, silo2);
    }

    [Fact]
    public void ConsistentHashRing_AddNode_MinimalRebalancing()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1"));
        ring.AddNode(new HashRingNode("silo-2"));

        // Record initial placements
        var initialPlacements = new Dictionary<string, string?>();
        for (int i = 0; i < 100; i++)
        {
            var key = $"actor-{i}";
            initialPlacements[key] = ring.GetNode(key);
        }

        // Act - Add a new node
        ring.AddNode(new HashRingNode("silo-3"));

        // Check how many actors moved
        var moved = 0;
        for (int i = 0; i < 100; i++)
        {
            var key = $"actor-{i}";
            if (ring.GetNode(key) != initialPlacements[key])
            {
                moved++;
            }
        }

        // Assert - Only about 1/3 should move (100/3 ≈ 33)
        // Allow 20-50% range due to hash distribution variance
        Assert.True(moved >= 20 && moved <= 50, $"Expected 20-50 actors to move, but {moved} moved");
    }

    [Fact]
    public void ConsistentHashRing_GetAllNodes_ReturnsAllSilos()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1"));
        ring.AddNode(new HashRingNode("silo-2"));
        ring.AddNode(new HashRingNode("silo-3"));

        // Act
        var nodes = ring.GetAllNodes();

        // Assert
        Assert.Equal(3, nodes.Count);
        Assert.Contains("silo-1", nodes);
        Assert.Contains("silo-2", nodes);
        Assert.Contains("silo-3", nodes);
    }

    [Fact]
    public void ConsistentHashRing_AddNode_IgnoresDuplicates()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        var node = new HashRingNode("silo-1");

        // Act
        ring.AddNode(node);
        ring.AddNode(node);

        // Assert
        Assert.Equal(1, ring.NodeCount);
    }

    [Fact]
    public void ConsistentHashRing_VirtualNodes_AffectsDistribution()
    {
        // Arrange
        var ring = new ConsistentHashRing();
        ring.AddNode(new HashRingNode("silo-1", virtualNodeCount: 50));
        ring.AddNode(new HashRingNode("silo-2", virtualNodeCount: 150)); // 3x more virtual nodes

        // Act - Distribute many actors
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var silo = ring.GetNode($"actor-{i}");
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - silo-2 should get more actors due to more virtual nodes
        Assert.True(placements["silo-2"] > placements["silo-1"],
            $"silo-2 ({placements["silo-2"]}) should have more actors than silo-1 ({placements["silo-1"]})");
    }
}
