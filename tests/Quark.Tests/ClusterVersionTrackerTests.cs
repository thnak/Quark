using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Migration;
using Quark.Core.Actors.Migration;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for ClusterVersionTracker - cluster-synchronized version tracking.
/// </summary>
public class ClusterVersionTrackerTests
{
    [Fact]
    public async Task RegisterSiloVersionsAsync_UpdatesClusterMembership()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        mockMembership.Setup(m => m.CurrentSiloId).Returns("silo-1");
        
        var existingSilo = new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active);
        mockMembership.Setup(m => m.GetSiloAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSilo);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0", "TestAssembly")
        };

        // Act
        await tracker.RegisterSiloVersionsAsync(versions);

        // Assert
        mockMembership.Verify(
            m => m.RegisterSiloAsync(
                It.Is<SiloInfo>(s => 
                    s.SiloId == "silo-1" &&
                    s.ActorTypeVersions != null &&
                    s.ActorTypeVersions.ContainsKey("TestActor")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSiloCapabilitiesAsync_ReturnsSiloVersions()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0", "TestAssembly")
        };
        var silo = new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active, 
            actorTypeVersions: versions);
        
        mockMembership.Setup(m => m.GetSiloAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(silo);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act
        var capabilities = await tracker.GetSiloCapabilitiesAsync("silo-1");

        // Assert
        Assert.NotNull(capabilities);
        Assert.Equal("silo-1", capabilities.SiloId);
        Assert.True(capabilities.SupportsActorType("TestActor", "1.0.0"));
    }

    [Fact]
    public async Task GetSiloCapabilitiesAsync_NoVersions_ReturnsNull()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        
        var silo = new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active);
        mockMembership.Setup(m => m.GetSiloAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(silo);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act
        var capabilities = await tracker.GetSiloCapabilitiesAsync("silo-1");

        // Assert
        Assert.Null(capabilities);
    }

    [Fact]
    public async Task GetAllSiloCapabilitiesAsync_ReturnsAllSilosWithVersions()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        
        var versions1 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        var versions2 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("2.0.0")
        };
        
        var silos = new List<SiloInfo>
        {
            new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active, actorTypeVersions: versions1),
            new SiloInfo("silo-2", "localhost", 5001, SiloStatus.Active, actorTypeVersions: versions2),
            new SiloInfo("silo-3", "localhost", 5002, SiloStatus.Active) // No versions
        };
        
        mockMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act
        var capabilities = await tracker.GetAllSiloCapabilitiesAsync();

        // Assert
        Assert.NotNull(capabilities);
        Assert.Equal(2, capabilities.Count); // Only silos with versions
        Assert.Contains(capabilities, c => c.SiloId == "silo-1");
        Assert.Contains(capabilities, c => c.SiloId == "silo-2");
    }

    [Fact]
    public async Task FindCompatibleSilosAsync_WithVersion_ReturnsMatchingSilos()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        
        var versions1 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        var versions2 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("2.0.0")
        };
        
        var silos = new List<SiloInfo>
        {
            new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active, actorTypeVersions: versions1),
            new SiloInfo("silo-2", "localhost", 5001, SiloStatus.Active, actorTypeVersions: versions2),
            new SiloInfo("silo-3", "localhost", 5002, SiloStatus.Active, actorTypeVersions: versions1)
        };
        
        mockMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act
        var compatibleSilos = await tracker.FindCompatibleSilosAsync("TestActor", "1.0.0");

        // Assert
        Assert.NotNull(compatibleSilos);
        Assert.Equal(2, compatibleSilos.Count);
        Assert.Contains("silo-1", compatibleSilos);
        Assert.Contains("silo-3", compatibleSilos);
    }

    [Fact]
    public async Task FindCompatibleSilosAsync_NoVersion_ReturnsAllSilosWithActorType()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        
        var versions1 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        var versions2 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("2.0.0")
        };
        var versions3 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["OtherActor"] = new AssemblyVersionInfo("1.0.0")
        };
        
        var silos = new List<SiloInfo>
        {
            new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active, actorTypeVersions: versions1),
            new SiloInfo("silo-2", "localhost", 5001, SiloStatus.Active, actorTypeVersions: versions2),
            new SiloInfo("silo-3", "localhost", 5002, SiloStatus.Active, actorTypeVersions: versions3)
        };
        
        mockMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act
        var compatibleSilos = await tracker.FindCompatibleSilosAsync("TestActor");

        // Assert
        Assert.NotNull(compatibleSilos);
        Assert.Equal(2, compatibleSilos.Count);
        Assert.Contains("silo-1", compatibleSilos);
        Assert.Contains("silo-2", compatibleSilos);
    }

    [Fact]
    public async Task GetActorTypeVersionAsync_AfterRegistration_ReturnsVersion()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        mockMembership.Setup(m => m.CurrentSiloId).Returns("silo-1");
        
        var existingSilo = new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active);
        mockMembership.Setup(m => m.GetSiloAsync("silo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSilo);
        
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0", "TestAssembly")
        };
        
        await tracker.RegisterSiloVersionsAsync(versions);

        // Act
        var version = await tracker.GetActorTypeVersionAsync("TestActor");

        // Assert
        Assert.NotNull(version);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal("TestAssembly", version.AssemblyName);
    }

    [Fact]
    public async Task RegisterSiloVersionsAsync_WithNullVersions_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = NullLogger<ClusterVersionTracker>.Instance;
        var mockMembership = new Mock<IClusterMembership>();
        var tracker = new ClusterVersionTracker(logger, mockMembership.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await tracker.RegisterSiloVersionsAsync(null!));
    }
}
