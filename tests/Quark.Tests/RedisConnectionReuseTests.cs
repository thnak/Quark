using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Persistence;
using Quark.Abstractions.Reminders;
using Quark.Clustering.Redis;
using Quark.Extensions.DependencyInjection;
using Quark.Hosting;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for Redis connection reuse and shared connection scenarios.
/// </summary>
public sealed class RedisConnectionReuseTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _sharedRedis;

    public async Task InitializeAsync()
    {
        // Start Redis container
        _redisContainer = new RedisBuilder("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // Create shared Redis connection
        _sharedRedis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _sharedRedis?.Dispose();

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    [Fact]
    public void WithRedisClustering_RegistersSharedConnection_WhenProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder.WithRedisClustering(connectionMultiplexer: _sharedRedis);

        var provider = services.BuildServiceProvider();

        // Assert
        var redis = provider.GetRequiredService<IConnectionMultiplexer>();
        Assert.Same(_sharedRedis, redis);
    }

    [Fact]
    public void WithRedisClustering_RegistersClusterMembership()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis);

        var provider = services.BuildServiceProvider();

        // Assert
        var membership = provider.GetRequiredService<IClusterMembership>();
        Assert.NotNull(membership);
        Assert.IsType<RedisClusterMembership>(membership);
    }

    [Fact]
    public void WithRedisClustering_RegistersHealthMonitor_WhenEnabled()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder
            .WithRedisClustering(
                connectionMultiplexer: _sharedRedis,
                enableHealthMonitoring: true);

        var provider = services.BuildServiceProvider();

        // Assert
        var healthMonitor = provider.GetRequiredService<RedisConnectionHealthMonitor>();
        Assert.NotNull(healthMonitor);
    }

    [Fact]
    public void WithRedisClustering_DoesNotRegisterHealthMonitor_WhenDisabled()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder
            .WithRedisClustering(
                connectionMultiplexer: _sharedRedis,
                enableHealthMonitoring: false);

        var provider = services.BuildServiceProvider();

        // Assert
        var healthMonitor = provider.GetService<RedisConnectionHealthMonitor>();
        Assert.Null(healthMonitor);
    }

    [Fact]
    public void WithRedisStateStorage_UsesSharedConnection()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis)
            .WithRedisStateStorage<TestState>();

        var provider = services.BuildServiceProvider();

        // Assert
        var stateStorage = provider.GetRequiredService<IStateStorage<TestState>>();
        Assert.NotNull(stateStorage);
        
        // Verify it uses the same Redis connection
        var redis = provider.GetRequiredService<IConnectionMultiplexer>();
        Assert.Same(_sharedRedis, redis);
    }

    [Fact]
    public void WithRedisReminderStorage_UsesSharedConnection()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act
        builder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis)
            .WithRedisReminderStorage();

        var provider = services.BuildServiceProvider();

        // Assert
        var reminderTable = provider.GetRequiredService<IReminderTable>();
        Assert.NotNull(reminderTable);
        
        // Verify it uses the same Redis connection
        var redis = provider.GetRequiredService<IConnectionMultiplexer>();
        Assert.Same(_sharedRedis, redis);
    }

    [Fact]
    public void WithRedisClustering_ThrowsException_WhenNoConnectionProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        builder
            .WithRedisClustering(); // No connection provided

        var provider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            provider.GetRequiredService<IConnectionMultiplexer>());
    }

    [Fact]
    public void CoHostedScenario_SharesRedisConnection_BetweenSiloAndClient()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add both Silo and Client using the shared connection
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "co-hosted-silo";
        });

        builder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis);

        var clientBuilder = services.AddQuarkClient(options =>
        {
            options.ClientId = "co-hosted-client";
        });

        clientBuilder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis);

        var provider = services.BuildServiceProvider();

        // Act
        var siloMembership = provider.GetRequiredService<IClusterMembership>();
        var clientMembership = provider.GetRequiredService<IClusterMembership>();
        var redis = provider.GetRequiredService<IConnectionMultiplexer>();

        // Assert
        Assert.Same(_sharedRedis, redis);
        Assert.NotNull(siloMembership);
        Assert.NotNull(clientMembership);
    }

    [Fact]
    public void MultipleComponents_ReusesSameConnection()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddQuarkSilo(options =>
        {
            options.SiloId = "test-silo";
        });

        // Act - register multiple Redis-dependent components
        builder
            .WithRedisClustering(connectionMultiplexer: _sharedRedis)
            .WithRedisStateStorage<TestState>()
            .WithRedisReminderStorage();

        var provider = services.BuildServiceProvider();

        // Assert - all components should use the same connection
        var redis1 = provider.GetRequiredService<IConnectionMultiplexer>();
        var redis2 = provider.GetRequiredService<IConnectionMultiplexer>();
        
        Assert.Same(_sharedRedis, redis1);
        Assert.Same(_sharedRedis, redis2);
        Assert.Same(redis1, redis2);
    }

    private class TestState
    {
        public string? Value { get; set; }
    }
}
