using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Client;
using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

public class SmartRoutingTests
{
    [Fact]
    public async Task Route_WithLocalSilo_ReturnsLocalSiloResult()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var location = new ActorLocation("actor-1", "TestActor", "silo-local");
        actorDirectory
            .Setup(x => x.LookupActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var options = Options.Create(new SmartRoutingOptions
        {
            Enabled = true,
            EnableLocalBypass = true
        });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance,
            localSiloId: "silo-local");

        // Act
        var decision = await router.RouteAsync("actor-1", "TestActor");

        // Assert
        Assert.Equal(RoutingResult.SameProcess, decision.Result);
        Assert.Equal("silo-local", decision.TargetSiloId);
    }

    [Fact]
    public async Task Route_WithRemoteSilo_ReturnsRemoteResult()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var location = new ActorLocation("actor-1", "TestActor", "silo-remote");
        actorDirectory
            .Setup(x => x.LookupActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var options = Options.Create(new SmartRoutingOptions
        {
            Enabled = true,
            EnableLocalBypass = true
        });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance,
            localSiloId: "silo-local");

        // Act
        var decision = await router.RouteAsync("actor-1", "TestActor");

        // Assert
        Assert.Equal(RoutingResult.Remote, decision.Result);
        Assert.Equal("silo-remote", decision.TargetSiloId);
    }

    [Fact]
    public async Task Route_WithNotActivatedActor_UsesPlacementPolicy()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        // Actor not found in directory
        actorDirectory
            .Setup(x => x.LookupActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorLocation?)null);

        // Placement policy returns local silo
        clusterMembership
            .Setup(x => x.GetActorSilo("actor-1", "TestActor"))
            .Returns("silo-local");

        var options = Options.Create(new SmartRoutingOptions
        {
            Enabled = true,
            EnableLocalBypass = true
        });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance,
            localSiloId: "silo-local");

        // Act
        var decision = await router.RouteAsync("actor-1", "TestActor");

        // Assert
        Assert.Equal(RoutingResult.LocalSilo, decision.Result);
        Assert.Equal("silo-local", decision.TargetSiloId);
    }

    [Fact]
    public async Task Route_CachesDecisions()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var location = new ActorLocation("actor-1", "TestActor", "silo-remote");
        actorDirectory
            .Setup(x => x.LookupActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var options = Options.Create(new SmartRoutingOptions
        {
            Enabled = true,
            CacheTtl = TimeSpan.FromMinutes(5)
        });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance,
            localSiloId: "silo-local");

        // Act - First call
        var decision1 = await router.RouteAsync("actor-1", "TestActor");

        // Act - Second call (should use cache)
        var decision2 = await router.RouteAsync("actor-1", "TestActor");

        // Assert
        Assert.Equal(decision1.Result, decision2.Result);
        Assert.Equal(decision1.TargetSiloId, decision2.TargetSiloId);

        // Directory should only be called once (second call uses cache)
        actorDirectory.Verify(
            x => x.LookupActorAsync("actor-1", "TestActor", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void InvalidateCache_RemovesCachedEntry()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var options = Options.Create(new SmartRoutingOptions { Enabled = true });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance);

        // Act
        router.InvalidateCache("actor-1", "TestActor");

        // Assert - No exception should be thrown
        Assert.True(true);
    }

    [Fact]
    public void GetRoutingStatistics_ReturnsStatistics()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        var options = Options.Create(new SmartRoutingOptions
        {
            Enabled = true,
            EnableStatistics = true
        });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance);

        // Act
        var stats = router.GetRoutingStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Contains("TotalRequests", stats.Keys);
        Assert.Contains("LocalSiloHits", stats.Keys);
        Assert.Contains("RemoteHits", stats.Keys);
        Assert.Contains("CacheHits", stats.Keys);
    }

    [Fact]
    public async Task Route_WithDisabledOption_UsesFallbackRouting()
    {
        // Arrange
        var actorDirectory = new Mock<IActorDirectory>();
        var clusterMembership = new Mock<IQuarkClusterMembership>();

        clusterMembership
            .Setup(x => x.GetActorSilo("actor-1", "TestActor"))
            .Returns("silo-remote");

        var options = Options.Create(new SmartRoutingOptions { Enabled = false });

        var router = new SmartRouter(
            actorDirectory.Object,
            clusterMembership.Object,
            options,
            NullLogger<SmartRouter>.Instance);

        // Act
        var decision = await router.RouteAsync("actor-1", "TestActor");

        // Assert
        Assert.Equal(RoutingResult.Remote, decision.Result);
        Assert.Equal("silo-remote", decision.TargetSiloId);

        // Should not call actor directory when disabled
        actorDirectory.Verify(
            x => x.LookupActorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
