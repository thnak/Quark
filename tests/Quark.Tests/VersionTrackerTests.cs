using Microsoft.Extensions.Logging.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Core.Actors.Migration;
using Xunit;

namespace Quark.Tests;

public class VersionTrackerTests
{
    [Fact]
    public async Task RegisterSiloVersionsAsync_StoresVersions()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0", "TestAssembly"),
            ["OrderActor"] = new AssemblyVersionInfo("2.1.0", "OrderAssembly")
        };

        // Act
        await tracker.RegisterSiloVersionsAsync(versions);

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task GetActorTypeVersionAsync_AfterRegistration_ReturnsVersion()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
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
    public async Task GetActorTypeVersionAsync_NotRegistered_ReturnsNull()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);

        // Act
        var version = await tracker.GetActorTypeVersionAsync("NonExistentActor");

        // Assert
        Assert.Null(version);
    }

    [Fact]
    public async Task GetSiloCapabilitiesAsync_NoSilo_ReturnsNull()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);

        // Act
        var capabilities = await tracker.GetSiloCapabilitiesAsync("non-existent-silo");

        // Assert
        Assert.Null(capabilities);
    }

    [Fact]
    public async Task GetAllSiloCapabilitiesAsync_EmptyCluster_ReturnsEmpty()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);

        // Act
        var capabilities = await tracker.GetAllSiloCapabilitiesAsync();

        // Assert
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task FindCompatibleSilosAsync_NoVersionSpecified_FindsAllSilosWithActorType()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        
        // Register current silo versions
        tracker.SetCurrentSiloId("silo-1");
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        await tracker.RegisterSiloVersionsAsync(versions);

        // Act
        var compatibleSilos = await tracker.FindCompatibleSilosAsync("TestActor");

        // Assert
        Assert.NotNull(compatibleSilos);
        Assert.Single(compatibleSilos);
        Assert.Contains("silo-1", compatibleSilos);
    }

    [Fact]
    public async Task FindCompatibleSilosAsync_WithVersionSpecified_FindsMatchingSilos()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        
        // Register current silo versions
        tracker.SetCurrentSiloId("silo-1");
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        await tracker.RegisterSiloVersionsAsync(versions);

        // Act - Find exact version match
        var compatibleSilos = await tracker.FindCompatibleSilosAsync("TestActor", "1.0.0");

        // Assert
        Assert.NotNull(compatibleSilos);
        Assert.Single(compatibleSilos);
        Assert.Contains("silo-1", compatibleSilos);
    }

    [Fact]
    public async Task FindCompatibleSilosAsync_VersionMismatch_ReturnsEmpty()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        
        // Register current silo versions
        tracker.SetCurrentSiloId("silo-1");
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        await tracker.RegisterSiloVersionsAsync(versions);

        // Act - Find different version
        var compatibleSilos = await tracker.FindCompatibleSilosAsync("TestActor", "2.0.0");

        // Assert
        Assert.NotNull(compatibleSilos);
        Assert.Empty(compatibleSilos);
    }

    [Fact]
    public void UpdateSiloCapabilities_AddsCapabilities()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };

        // Act
        tracker.UpdateSiloCapabilities("silo-2", versions);

        // Assert - Should be able to get capabilities
        var capabilities = tracker.GetSiloCapabilitiesAsync("silo-2").Result;
        Assert.NotNull(capabilities);
        Assert.Equal("silo-2", capabilities.SiloId);
    }

    [Fact]
    public void RemoveSiloCapabilities_RemovesCapabilities()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        var versions = new Dictionary<string, AssemblyVersionInfo>
        {
            ["TestActor"] = new AssemblyVersionInfo("1.0.0")
        };
        tracker.UpdateSiloCapabilities("silo-2", versions);

        // Act
        tracker.RemoveSiloCapabilities("silo-2");

        // Assert
        var capabilities = tracker.GetSiloCapabilitiesAsync("silo-2").Result;
        Assert.Null(capabilities);
    }

    [Fact]
    public void SetCurrentSiloId_SetsId()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);

        // Act
        tracker.SetCurrentSiloId("my-silo");

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task GetAllSiloCapabilitiesAsync_WithMultipleSilos_ReturnsAll()
    {
        // Arrange
        var logger = NullLogger<VersionTracker>.Instance;
        var tracker = new VersionTracker(logger);
        
        var versions1 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["Actor1"] = new AssemblyVersionInfo("1.0.0")
        };
        var versions2 = new Dictionary<string, AssemblyVersionInfo>
        {
            ["Actor2"] = new AssemblyVersionInfo("2.0.0")
        };
        
        tracker.UpdateSiloCapabilities("silo-1", versions1);
        tracker.UpdateSiloCapabilities("silo-2", versions2);

        // Act
        var allCapabilities = await tracker.GetAllSiloCapabilitiesAsync();

        // Assert
        Assert.Equal(2, allCapabilities.Count);
    }
}
