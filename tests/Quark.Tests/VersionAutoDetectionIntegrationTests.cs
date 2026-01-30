using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Core.Actors;
using Quark.Extensions.DependencyInjection;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Integration tests for automatic version detection and registration.
/// </summary>
public class VersionAutoDetectionIntegrationTests
{
    [Actor(Name = "TestActorForVersionDetection")]
    public class TestVersionActor : ActorBase
    {
        public TestVersionActor(string actorId) : base(actorId) { }
        
        public Task<string> GetMessageAsync() => Task.FromResult("Hello from TestVersionActor");
    }

    [Fact]
    public async Task RegisterActorVersions_WithGeneratedRegistry_RegistersVersionsAutomatically()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IActorFactory, ActorFactory>();
        
        // Add version-aware placement and register auto-detected versions
        services.AddVersionAwarePlacement();
        services.RegisterActorVersions(Quark.Generated.ActorVersionRegistry.VersionMap);
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Start hosted services (triggers version registration)
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }
        
        var versionTracker = serviceProvider.GetRequiredService<IVersionTracker>();
        
        // Act - Get version for our test actor
        var version = await versionTracker.GetActorTypeVersionAsync("TestVersionActor");
        
        // Assert
        Assert.NotNull(version);
        Assert.Equal("0.1.0", version.Version);
        Assert.Equal("Quark.Tests", version.AssemblyName);
        
        // Cleanup
        foreach (var service in hostedServices)
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
    
    [Fact]
    public async Task RegisterActorVersions_DiscoverMultipleActorTypes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddVersionAwarePlacement();
        services.RegisterActorVersions(Quark.Generated.ActorVersionRegistry.VersionMap);
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Start hosted services
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }
        
        var versionTracker = serviceProvider.GetRequiredService<IVersionTracker>();
        
        // Act - Get all actor types from the registry
        var actorTypes = Quark.Generated.ActorVersionRegistry.GetAllActorTypes();
        
        // Assert - Should have multiple actor types from test assembly
        Assert.NotNull(actorTypes);
        Assert.NotEmpty(actorTypes);
        
        // All actor types should have version information
        foreach (var actorType in actorTypes)
        {
            var version = await versionTracker.GetActorTypeVersionAsync(actorType);
            Assert.NotNull(version);
            Assert.False(string.IsNullOrEmpty(version.Version));
        }
        
        // Cleanup
        foreach (var service in hostedServices)
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
    
    [Fact]
    public async Task FindCompatibleSilos_WithRegisteredVersions_FindsCurrentSilo()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddVersionAwarePlacement();
        services.RegisterActorVersions(Quark.Generated.ActorVersionRegistry.VersionMap);
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Start hosted services
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }
        
        var versionTracker = serviceProvider.GetRequiredService<IVersionTracker>();
        
        // Set current silo ID
        if (versionTracker is Quark.Core.Actors.Migration.VersionTracker tracker)
        {
            tracker.SetCurrentSiloId("test-silo-1");
        }
        
        // Act - Find compatible silos for an actor type
        var actorTypes = Quark.Generated.ActorVersionRegistry.GetAllActorTypes();
        if (actorTypes.Count > 0)
        {
            var firstActorType = actorTypes.First();
            var compatibleSilos = await versionTracker.FindCompatibleSilosAsync(firstActorType);
            
            // Assert - Current silo should be in the list
            Assert.NotNull(compatibleSilos);
            Assert.Contains("test-silo-1", compatibleSilos);
        }
        
        // Cleanup
        foreach (var service in hostedServices)
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
