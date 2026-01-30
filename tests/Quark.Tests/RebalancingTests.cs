using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;
using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

public class RebalancingTests
{
    [Fact]
    public async Task EvaluateRebalancing_WithBalancedLoad_ReturnsEmptyDecisions()
    {
        // Arrange
        var healthMonitor = new Mock<IClusterHealthMonitor>();
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var silos = new List<SiloInfo>
        {
            new("silo-1", "host1", 5000),
            new("silo-2", "host2", 5001)
        };

        clusterMembership
            .Setup(x => x.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);

        // Both silos have similar health scores (balanced)
        healthMonitor
            .Setup(x => x.GetHealthScoreAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiloHealthScore(50, 50, 10, DateTimeOffset.UtcNow));

        healthMonitor
            .Setup(x => x.GetHealthScoreAsync("silo-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiloHealthScore(50, 50, 10, DateTimeOffset.UtcNow));

        var options = Options.Create(new RebalancingOptions { Enabled = true });
        var rebalancer = new LoadBasedRebalancer(
            healthMonitor.Object,
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<LoadBasedRebalancer>.Instance);

        // Act
        var decisions = await rebalancer.EvaluateRebalancingAsync();

        // Assert
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task EvaluateRebalancing_WithLoadImbalance_ReturnsDecisions()
    {
        // Arrange
        var healthMonitor = new Mock<IClusterHealthMonitor>();
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var silos = new List<SiloInfo>
        {
            new("silo-1", "host1", 5000),
            new("silo-2", "host2", 5001)
        };

        clusterMembership
            .Setup(x => x.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);

        // Silo-1 is overloaded (high CPU/memory usage = low health score)
        healthMonitor
            .Setup(x => x.GetHealthScoreAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiloHealthScore(80, 80, 100, DateTimeOffset.UtcNow));

        // Silo-2 is underloaded (low CPU/memory usage = high health score)
        healthMonitor
            .Setup(x => x.GetHealthScoreAsync("silo-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiloHealthScore(10, 10, 10, DateTimeOffset.UtcNow));

        // Silo-1 has some actors
        var actors = new List<ActorLocation>
        {
            new("actor-1", "TestActor", "silo-1"),
            new("actor-2", "TestActor", "silo-1")
        };

        actorDirectory
            .Setup(x => x.GetActorsBySiloAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(actors);

        actorDirectory
            .Setup(x => x.GetActorsBySiloAsync("silo-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActorLocation>());

        var options = Options.Create(new RebalancingOptions
        {
            Enabled = true,
            LoadImbalanceThreshold = 0.3,
            MaxMigrationCost = 0.9
        });

        var rebalancer = new LoadBasedRebalancer(
            healthMonitor.Object,
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<LoadBasedRebalancer>.Instance);

        // Act
        var decisions = await rebalancer.EvaluateRebalancingAsync();

        // Assert
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d =>
        {
            Assert.Equal("silo-1", d.SourceSiloId);
            Assert.Equal("silo-2", d.TargetSiloId);
            Assert.Equal(RebalancingReason.LoadImbalance, d.Reason);
        });
    }

    [Fact]
    public async Task ExecuteRebalancing_UpdatesActorLocation()
    {
        // Arrange
        var healthMonitor = new Mock<IClusterHealthMonitor>();
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var options = Options.Create(new RebalancingOptions { Enabled = true });
        var rebalancer = new LoadBasedRebalancer(
            healthMonitor.Object,
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<LoadBasedRebalancer>.Instance);

        var decision = new RebalancingDecision(
            "actor-1",
            "TestActor",
            "silo-1",
            "silo-2",
            RebalancingReason.LoadImbalance,
            0.5);

        // Act
        var result = await rebalancer.ExecuteRebalancingAsync(decision);

        // Assert
        Assert.True(result);

        actorDirectory.Verify(
            x => x.UnregisterActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()),
            Times.Once);

        actorDirectory.Verify(
            x => x.RegisterActorAsync(
                It.Is<ActorLocation>(loc =>
                    loc.ActorId == "actor-1" &&
                    loc.ActorType == "TestActor" &&
                    loc.SiloId == "silo-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CalculateMigrationCost_ReturnsValidCost()
    {
        // Arrange
        var healthMonitor = new Mock<IClusterHealthMonitor>();
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var options = Options.Create(new RebalancingOptions { Enabled = true });
        var rebalancer = new LoadBasedRebalancer(
            healthMonitor.Object,
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<LoadBasedRebalancer>.Instance);

        // Act
        var cost = await rebalancer.CalculateMigrationCostAsync("actor-1", "TestActor");

        // Assert
        Assert.InRange(cost, 0.0, 1.0);
    }

    [Fact]
    public async Task EvaluateRebalancing_WithDisabledOption_ReturnsEmpty()
    {
        // Arrange
        var healthMonitor = new Mock<IClusterHealthMonitor>();
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var options = Options.Create(new RebalancingOptions { Enabled = false });
        var rebalancer = new LoadBasedRebalancer(
            healthMonitor.Object,
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<LoadBasedRebalancer>.Instance);

        // Act
        var decisions = await rebalancer.EvaluateRebalancingAsync();

        // Assert
        Assert.Empty(decisions);
    }
}
