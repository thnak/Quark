using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

public class PlacementPolicyTests
{
    [Fact]
    public void RandomPlacement_DistributesAcrossSilos()
    {
        // Arrange
        var policy = new RandomPlacementPolicy();
        var silos = new List<string> { "silo-1", "silo-2", "silo-3" };
        var placements = new Dictionary<string, int>();

        // Act - Place many actors
        for (int i = 0; i < 300; i++)
        {
            var silo = policy.SelectSilo($"actor-{i}", "TestActor", silos);
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Each silo should get some actors (rough check)
        Assert.Equal(3, placements.Count);
        foreach (var count in placements.Values)
        {
            Assert.True(count > 50, $"Expected > 50 actors, got {count}");
        }
    }

    [Fact]
    public void RandomPlacement_ReturnsNullWhenNoSilos()
    {
        // Arrange
        var policy = new RandomPlacementPolicy();

        // Act
        var silo = policy.SelectSilo("actor-1", "TestActor", Array.Empty<string>());

        // Assert
        Assert.Null(silo);
    }

    [Fact]
    public void LocalPreferredPlacement_PrefersLocalSilo()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-2"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        var policy = new LocalPreferredPlacementPolicy("silo-2", hashRing);
        var silos = new List<string> { "silo-1", "silo-2", "silo-3" };

        // Act
        var silo = policy.SelectSilo("actor-123", "TestActor", silos);

        // Assert
        Assert.Equal("silo-2", silo);
    }

    [Fact]
    public void LocalPreferredPlacement_FallsBackToHashRing()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        var policy = new LocalPreferredPlacementPolicy("silo-2", hashRing);
        var silos = new List<string> { "silo-1", "silo-3" }; // Local silo not available

        // Act
        var silo = policy.SelectSilo("actor-123", "TestActor", silos);

        // Assert - Should use hash ring
        Assert.NotNull(silo);
        Assert.Contains(silo, silos);
    }

    [Fact]
    public void StatelessWorkerPlacement_UsesRoundRobin()
    {
        // Arrange
        var policy = new StatelessWorkerPlacementPolicy();
        var silos = new List<string> { "silo-1", "silo-2", "silo-3" };
        var placements = new List<string>();

        // Act - Place 9 actors
        for (int i = 0; i < 9; i++)
        {
            var silo = policy.SelectSilo($"actor-{i}", "WorkerActor", silos);
            if (silo != null)
            {
                placements.Add(silo);
            }
        }

        // Assert - Should cycle through silos (3 times each)
        Assert.Equal(9, placements.Count);
        Assert.Equal(3, placements.Count(s => s == "silo-1"));
        Assert.Equal(3, placements.Count(s => s == "silo-2"));
        Assert.Equal(3, placements.Count(s => s == "silo-3"));
    }

    [Fact]
    public void ConsistentHashPlacement_AlwaysReturnsSameSilo()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-2"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        var policy = new ConsistentHashPlacementPolicy(hashRing);
        var silos = new List<string> { "silo-1", "silo-2", "silo-3" };

        // Act - Place same actor multiple times
        var silo1 = policy.SelectSilo("actor-456", "TestActor", silos);
        var silo2 = policy.SelectSilo("actor-456", "TestActor", silos);
        var silo3 = policy.SelectSilo("actor-456", "TestActor", silos);

        // Assert - Should always return same silo
        Assert.Equal(silo1, silo2);
        Assert.Equal(silo2, silo3);
    }

    [Fact]
    public void ConsistentHashPlacement_DifferentActorsDistribute()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-2"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        var policy = new ConsistentHashPlacementPolicy(hashRing);
        var silos = new List<string> { "silo-1", "silo-2", "silo-3" };
        var placements = new Dictionary<string, int>();

        // Act - Place many different actors
        for (int i = 0; i < 300; i++)
        {
            var silo = policy.SelectSilo($"actor-{i}", "TestActor", silos);
            if (silo != null)
            {
                placements[silo] = placements.GetValueOrDefault(silo) + 1;
            }
        }

        // Assert - Should distribute across all silos
        Assert.Equal(3, placements.Count);
        foreach (var count in placements.Values)
        {
            Assert.True(count > 50, $"Expected > 50 actors per silo, got {count}");
        }
    }

    [Fact]
    public void AllPolicies_HandleEmptySiloList()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        var policies = new IPlacementPolicy[]
        {
            new RandomPlacementPolicy(),
            new LocalPreferredPlacementPolicy("silo-1", hashRing),
            new StatelessWorkerPlacementPolicy(),
            new ConsistentHashPlacementPolicy(hashRing)
        };

        // Act & Assert
        foreach (var policy in policies)
        {
            var silo = policy.SelectSilo("actor-1", "TestActor", Array.Empty<string>());
            Assert.Null(silo);
        }
    }
}
