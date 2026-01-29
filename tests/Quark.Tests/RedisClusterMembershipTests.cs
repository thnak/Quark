using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;
using Quark.Networking.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Quark.Tests;

/// <summary>
/// Integration tests for Redis cluster membership using Testcontainers.
/// </summary>
public class RedisClusterMembershipTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        // Start Redis container
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // Connect to Redis
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task RegisterSilo_StoresInRedis()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var siloInfo = new SiloInfo("silo-1", "localhost", 5000);

        // Act
        await membership.RegisterSiloAsync(siloInfo);

        // Assert
        var db = _redis!.GetDatabase();
        var exists = await db.KeyExistsAsync("quark:silo:silo-1");
        Assert.True(exists);
    }

    [Fact]
    public async Task GetActiveSilos_ReturnsRegisteredSilos()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var silo1 = new SiloInfo("silo-1", "localhost", 5000);
        var silo2 = new SiloInfo("silo-2", "localhost", 5001);

        await membership.RegisterSiloAsync(silo1);
        await membership.RegisterSiloAsync(silo2);

        // Act
        var silos = await membership.GetActiveSilosAsync();

        // Assert
        Assert.Equal(2, silos.Count);
        Assert.Contains(silos, s => s.SiloId == "silo-1");
        Assert.Contains(silos, s => s.SiloId == "silo-2");
    }

    [Fact]
    public async Task GetSilo_ReturnsCorrectSilo()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var siloInfo = new SiloInfo("silo-test", "localhost", 5000);
        await membership.RegisterSiloAsync(siloInfo);

        // Act
        var retrieved = await membership.GetSiloAsync("silo-test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("silo-test", retrieved!.SiloId);
        Assert.Equal("localhost", retrieved.Address);
        Assert.Equal(5000, retrieved.Port);
    }

    [Fact]
    public async Task UnregisterSilo_RemovesFromRedis()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var siloInfo = new SiloInfo("silo-1", "localhost", 5000);
        await membership.RegisterSiloAsync(siloInfo);

        // Act
        await membership.UnregisterSiloAsync();

        // Assert
        var db = _redis!.GetDatabase();
        var exists = await db.KeyExistsAsync("quark:silo:silo-1");
        Assert.False(exists);
    }

    [Fact]
    public async Task UpdateHeartbeat_UpdatesTimestamp()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var siloInfo = new SiloInfo("silo-1", "localhost", 5000);
        await membership.RegisterSiloAsync(siloInfo);

        var originalHeartbeat = siloInfo.LastHeartbeat;
        await Task.Delay(100);

        // Act
        await membership.UpdateHeartbeatAsync();

        // Assert
        var updated = await membership.GetSiloAsync("silo-1");
        Assert.NotNull(updated);
        Assert.True(updated!.LastHeartbeat > originalHeartbeat);
    }

    [Fact]
    public async Task HashRing_IntegratesWithMembership()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var silo1 = new SiloInfo("silo-1", "localhost", 5000);
        var silo2 = new SiloInfo("silo-2", "localhost", 5001);

        // Act
        await membership.RegisterSiloAsync(silo1);
        await membership.RegisterSiloAsync(silo2);

        // Assert - Hash ring should have both silos
        Assert.Equal(2, membership.HashRing.NodeCount);
        
        // Test actor placement
        var actorSilo = membership.GetActorSilo("actor-123", "TestActor");
        Assert.NotNull(actorSilo);
        Assert.Contains(actorSilo, new[] { "silo-1", "silo-2" });
    }

    [Fact]
    public async Task GetActorSilo_ConsistentPlacement()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var silo1 = new SiloInfo("silo-1", "localhost", 5000);
        var silo2 = new SiloInfo("silo-2", "localhost", 5001);

        await membership.RegisterSiloAsync(silo1);
        await membership.RegisterSiloAsync(silo2);

        // Act - Get placement multiple times
        var placement1 = membership.GetActorSilo("actor-456", "TestActor");
        var placement2 = membership.GetActorSilo("actor-456", "TestActor");
        var placement3 = membership.GetActorSilo("actor-456", "TestActor");

        // Assert - Should always return same silo
        Assert.Equal(placement1, placement2);
        Assert.Equal(placement2, placement3);
    }

    [Fact]
    public async Task StartAsync_LoadsExistingSilos()
    {
        // Arrange - Register a silo directly in Redis
        var membership1 = new RedisClusterMembership(_redis!, "silo-1");
        var siloInfo = new SiloInfo("silo-1", "localhost", 5000);
        await membership1.RegisterSiloAsync(siloInfo);

        // Act - Create new membership and start it
        var membership2 = new RedisClusterMembership(_redis!, "silo-2", new ConsistentHashRing());
        await membership2.StartAsync();

        // Assert - Should have loaded silo-1
        Assert.Equal(1, membership2.HashRing.NodeCount);
    }

    [Fact]
    public async Task MembershipChange_RaisesEvents()
    {
        // Arrange
        var membership1 = new RedisClusterMembership(_redis!, "silo-1");
        var membership2 = new RedisClusterMembership(_redis!, "silo-2");

        await membership1.StartAsync();
        await membership2.StartAsync();

        var joinedTcs = new TaskCompletionSource<SiloInfo>();
        membership1.SiloJoined += (sender, silo) => joinedTcs.TrySetResult(silo);

        // Act - Register new silo
        var silo3 = new SiloInfo("silo-3", "localhost", 5002);
        await membership2.RegisterSiloAsync(silo3);

        // Assert - Should receive event (with timeout)
        var joinedSilo = await joinedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("silo-3", joinedSilo.SiloId);
    }
}
