using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Placement.Memory;
using Xunit;

namespace Quark.Tests;

public class MemoryAwarePlacementTests
{
    [Fact]
    public void MemoryMonitor_RecordActorMemoryUsage_TracksMemory()
    {
        // Arrange
        var monitor = new MemoryMonitor();

        // Act
        monitor.RecordActorMemoryUsage("actor-1", "TestActor", 1024 * 1024); // 1 MB

        // Assert
        var usage = monitor.GetActorMemoryUsage("actor-1");
        Assert.Equal(1024 * 1024, usage);
    }

    [Fact]
    public void MemoryMonitor_GetSiloMemoryMetrics_ReturnsMetrics()
    {
        // Arrange
        var monitor = new MemoryMonitor();

        // Act
        var metrics = monitor.GetSiloMemoryMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.TotalMemoryBytes > 0);
        Assert.True(metrics.MemoryPressure >= 0.0 && metrics.MemoryPressure <= 1.0);
    }

    [Fact]
    public async Task MemoryMonitor_GetTopMemoryConsumers_ReturnsTopActors()
    {
        // Arrange
        var monitor = new MemoryMonitor();
        monitor.RecordActorMemoryUsage("actor-1", "TestActor", 5 * 1024 * 1024); // 5 MB
        monitor.RecordActorMemoryUsage("actor-2", "TestActor", 3 * 1024 * 1024); // 3 MB
        monitor.RecordActorMemoryUsage("actor-3", "TestActor", 1 * 1024 * 1024); // 1 MB

        // Act
        var topConsumers = await monitor.GetTopMemoryConsumersAsync(2);

        // Assert
        Assert.Equal(2, topConsumers.Count);
        Assert.Equal("actor-1", topConsumers[0].ActorId);
        Assert.Equal(5 * 1024 * 1024, topConsumers[0].MemoryBytes);
    }

    [Fact]
    public void MemoryAwarePlacementPolicy_SelectSilo_ReturnsNullWhenNoSilosAvailable()
    {
        // Arrange
        var monitorMock = new Mock<IMemoryMonitor>();
        monitorMock
            .Setup(m => m.GetSiloMemoryMetrics())
            .Returns(new MemoryMetrics { MemoryPressure = 0.5 });

        var loggerMock = new Mock<ILogger<MemoryAwarePlacementPolicy>>();
        var options = Options.Create(new MemoryAwarePlacementOptions());
        
        var policy = new MemoryAwarePlacementPolicy(
            monitorMock.Object,
            options,
            loggerMock.Object);

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", Array.Empty<string>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MemoryAwarePlacementPolicy_SelectSilo_ReturnsSiloWhenMemoryIsHealthy()
    {
        // Arrange
        var monitorMock = new Mock<IMemoryMonitor>();
        monitorMock
            .Setup(m => m.GetSiloMemoryMetrics())
            .Returns(new MemoryMetrics { MemoryPressure = 0.5 });

        var loggerMock = new Mock<ILogger<MemoryAwarePlacementPolicy>>();
        var options = Options.Create(new MemoryAwarePlacementOptions());
        
        var policy = new MemoryAwarePlacementPolicy(
            monitorMock.Object,
            options,
            loggerMock.Object);

        var silos = new[] { "silo-1", "silo-2" };

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", silos);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, silos);
    }

    [Fact]
    public void MemoryAwarePlacementPolicy_SelectSilo_RejectsWhenMemoryIsCritical()
    {
        // Arrange
        var monitorMock = new Mock<IMemoryMonitor>();
        monitorMock
            .Setup(m => m.GetSiloMemoryMetrics())
            .Returns(new MemoryMetrics 
            { 
                MemoryPressure = 0.9,
                TotalMemoryBytes = 2L * 1024 * 1024 * 1024 // 2 GB (over critical threshold)
            });

        var loggerMock = new Mock<ILogger<MemoryAwarePlacementPolicy>>();
        var options = Options.Create(new MemoryAwarePlacementOptions
        {
            CriticalThresholdBytes = 1536L * 1024 * 1024, // 1.5 GB
            RejectPlacementOnCriticalMemory = true
        });
        
        var policy = new MemoryAwarePlacementPolicy(
            monitorMock.Object,
            options,
            loggerMock.Object);

        var silos = new[] { "silo-1" };

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", silos);

        // Assert
        Assert.Null(result); // Should reject placement
    }

    [Fact]
    public async Task MemoryRebalancingCoordinator_EvaluateRebalancingAsync_ReturnsEmptyWhenMemoryIsHealthy()
    {
        // Arrange
        var monitorMock = new Mock<IMemoryMonitor>();
        monitorMock
            .Setup(m => m.GetSiloMemoryMetrics())
            .Returns(new MemoryMetrics { MemoryPressure = 0.5 });

        var directoryMock = new Mock<IActorDirectory>();
        var loggerMock = new Mock<ILogger<MemoryRebalancingCoordinator>>();
        var options = Options.Create(new MemoryAwarePlacementOptions());
        
        var coordinator = new MemoryRebalancingCoordinator(
            monitorMock.Object,
            directoryMock.Object,
            options,
            loggerMock.Object);

        // Act
        var decisions = await coordinator.EvaluateRebalancingAsync();

        // Assert
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task MemoryRebalancingCoordinator_CalculateMigrationCostAsync_ReturnsReasonableCost()
    {
        // Arrange
        var monitorMock = new Mock<IMemoryMonitor>();
        monitorMock
            .Setup(m => m.GetActorMemoryUsage("actor-1"))
            .Returns(10 * 1024 * 1024); // 10 MB

        var directoryMock = new Mock<IActorDirectory>();
        var loggerMock = new Mock<ILogger<MemoryRebalancingCoordinator>>();
        var options = Options.Create(new MemoryAwarePlacementOptions());
        
        var coordinator = new MemoryRebalancingCoordinator(
            monitorMock.Object,
            directoryMock.Object,
            options,
            loggerMock.Object);

        // Act
        var cost = await coordinator.CalculateMigrationCostAsync("actor-1", "TestActor");

        // Assert
        Assert.True(cost >= 0.0 && cost <= 1.0);
    }
}
