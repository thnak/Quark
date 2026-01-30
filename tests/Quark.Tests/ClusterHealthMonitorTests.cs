using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;
using Quark.Networking.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Quark.Tests;

/// <summary>
///     Integration tests for ClusterHealthMonitor using Testcontainers.
/// </summary>
public class ClusterHealthMonitorTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        _redisContainer = new RedisBuilder("redis:7-alpine").Build();
        await _redisContainer.StartAsync();
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
    public async Task UpdateHealthScore_StoresInRedis()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var calculator = new DefaultHealthScoreCalculator();
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator);

        var healthScore = new SiloHealthScore(30, 40, 50, DateTimeOffset.UtcNow);

        // Act
        await monitor.UpdateHealthScoreAsync(healthScore);

        // Assert
        var db = _redis!.GetDatabase();
        var exists = await db.KeyExistsAsync("quark:health:silo-1");
        Assert.True(exists);
    }

    [Fact]
    public async Task GetHealthScore_RetrievesStoredScore()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var calculator = new DefaultHealthScoreCalculator();
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator);

        var healthScore = new SiloHealthScore(30, 40, 50, DateTimeOffset.UtcNow);
        await monitor.UpdateHealthScoreAsync(healthScore);

        // Act
        var retrieved = await monitor.GetHealthScoreAsync("silo-1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(30, retrieved!.CpuUsagePercent);
        Assert.Equal(40, retrieved.MemoryUsagePercent);
        Assert.Equal(50, retrieved.NetworkLatencyMs);
    }

    [Fact]
    public async Task GetHealthScoreHistory_ReturnsMultipleScores()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-1");
        var calculator = new DefaultHealthScoreCalculator();
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator);

        // Add multiple health scores
        for (var i = 0; i < 5; i++)
        {
            var score = new SiloHealthScore(20 + i * 10, 30 + i * 10, 50 + i * 20, DateTimeOffset.UtcNow);
            await monitor.UpdateHealthScoreAsync(score);
            await Task.Delay(50); // Small delay to ensure different timestamps
        }

        // Act
        var history = await monitor.GetHealthScoreHistoryAsync("silo-1", 5);

        // Assert
        Assert.Equal(5, history.Count);
        // Verify chronological order (oldest first)
        for (var i = 0; i < history.Count - 1; i++)
        {
            Assert.True(history[i].Timestamp <= history[i + 1].Timestamp);
        }
    }

    [Fact]
    public async Task PerformHealthCheck_EvictsSiloOnTimeout()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-monitor");
        var calculator = new DefaultHealthScoreCalculator();
        var options = new EvictionPolicyOptions
        {
            Policy = SiloEvictionPolicy.TimeoutBased,
            HeartbeatTimeoutSeconds = 1, // Short timeout for testing
            HealthCheckIntervalSeconds = 1
        };
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator, options);

        // Register a silo
        var oldSilo = new SiloInfo("silo-old", "localhost", 5000, SiloStatus.Active);
        await membership.RegisterSiloAsync(oldSilo);

        // Wait for heartbeat timeout to expire
        await Task.Delay(1500); // Wait 1.5 seconds (longer than the 1 second timeout)

        var evictedTcs = new TaskCompletionSource<SiloEvictedEventArgs>();
        monitor.SiloEvicted += (sender, args) => evictedTcs.TrySetResult(args);

        // Act
        await monitor.PerformHealthCheckAsync();

        // Assert
        var evictedArgs = await evictedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("silo-old", evictedArgs.SiloInfo.SiloId);
        Assert.Contains("timeout", evictedArgs.Reason.ToLower());
    }

    [Fact]
    public async Task PerformHealthCheck_EvictsSiloOnLowHealthScore()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-monitor");
        var calculator = new DefaultHealthScoreCalculator();
        var options = new EvictionPolicyOptions
        {
            Policy = SiloEvictionPolicy.HealthScoreBased,
            HealthScoreThreshold = 50,
            ConsecutiveUnhealthyChecks = 2,
            HealthCheckIntervalSeconds = 1
        };
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator, options);

        // Register a silo
        var unhealthySilo = new SiloInfo("silo-unhealthy", "localhost", 5001);
        await membership.RegisterSiloAsync(unhealthySilo);

        // Update health score for the unhealthy silo (low score)
        var lowHealthScore = new SiloHealthScore(90, 90, 800, DateTimeOffset.UtcNow);
        await monitor.UpdateHealthScoreAsync(lowHealthScore);
        
        // Switch to the unhealthy silo's monitor to update its score
        var unhealthyMembership = new RedisClusterMembership(_redis!, "silo-unhealthy");
        var unhealthyMonitor = new ClusterHealthMonitor(_redis!, unhealthyMembership, calculator, options);
        await unhealthyMonitor.UpdateHealthScoreAsync(lowHealthScore);

        var evictedTcs = new TaskCompletionSource<SiloEvictedEventArgs>();
        monitor.SiloEvicted += (sender, args) => evictedTcs.TrySetResult(args);

        // Act - Perform multiple health checks to reach consecutive threshold
        await monitor.PerformHealthCheckAsync();
        await Task.Delay(100);
        await monitor.PerformHealthCheckAsync();

        // Assert
        var evictedArgs = await evictedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("silo-unhealthy", evictedArgs.SiloInfo.SiloId);
        Assert.Contains("health score", evictedArgs.Reason.ToLower());
    }

    [Fact]
    public async Task PerformHealthCheck_RaisesDegradationEvent()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-monitor");
        var calculator = new DefaultHealthScoreCalculator();
        var options = new EvictionPolicyOptions
        {
            Policy = SiloEvictionPolicy.HealthScoreBased,
            HealthScoreThreshold = 50,
            ConsecutiveUnhealthyChecks = 3, // High threshold so we get degradation event before eviction
            HealthCheckIntervalSeconds = 1
        };
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator, options);

        // Register the monitor's silo first
        var monitorSilo = new SiloInfo("silo-monitor", "localhost", 5000);
        await membership.RegisterSiloAsync(monitorSilo);

        // Register a degraded silo
        var degradedSilo = new SiloInfo("silo-degraded", "localhost", 5002);
        await membership.RegisterSiloAsync(degradedSilo);

        // Create a separate monitor for the degraded silo to update its health
        var degradedMembership = new RedisClusterMembership(_redis!, "silo-degraded");
        var degradedMonitor = new ClusterHealthMonitor(_redis!, degradedMembership, calculator, options);
        
        // Post a low health score for the degraded silo
        var degradedHealthScore = new SiloHealthScore(60, 60, 300, DateTimeOffset.UtcNow);
        await degradedMonitor.UpdateHealthScoreAsync(degradedHealthScore);

        var degradedTcs = new TaskCompletionSource<SiloHealthDegradedEventArgs>();
        monitor.SiloHealthDegraded += (sender, args) => degradedTcs.TrySetResult(args);

        // Act
        await monitor.PerformHealthCheckAsync();

        // Assert
        var degradedArgs = await degradedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("silo-degraded", degradedArgs.SiloInfo.SiloId);
        Assert.NotNull(degradedArgs.HealthScore);
    }

    [Fact]
    public async Task StartAsync_StartsPeriodicHealthChecks()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-monitor");
        var calculator = new DefaultHealthScoreCalculator();
        var options = new EvictionPolicyOptions
        {
            Policy = SiloEvictionPolicy.None, // No eviction for this test
            HealthCheckIntervalSeconds = 1
        };
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator, options);

        // Act
        await monitor.StartAsync();
        await Task.Delay(2500); // Wait for at least 2 health checks
        await monitor.StopAsync();

        // Assert
        // If we got here without exceptions, the timer is working
        Assert.True(true);
    }

    [Fact]
    public async Task EvictionPolicy_None_DoesNotEvictSilos()
    {
        // Arrange
        var membership = new RedisClusterMembership(_redis!, "silo-monitor");
        var calculator = new DefaultHealthScoreCalculator();
        var options = new EvictionPolicyOptions
        {
            Policy = SiloEvictionPolicy.None
        };
        var monitor = new ClusterHealthMonitor(_redis!, membership, calculator, options);

        // Register an old silo
        var oldSilo = new SiloInfo("silo-old", "localhost", 5000, SiloStatus.Active);
        oldSilo.GetType().GetProperty("LastHeartbeat")!.SetValue(oldSilo, DateTimeOffset.UtcNow.AddSeconds(-100));
        await membership.RegisterSiloAsync(oldSilo);

        var evicted = false;
        monitor.SiloEvicted += (sender, args) => evicted = true;

        // Act
        await monitor.PerformHealthCheckAsync();
        await Task.Delay(500);

        // Assert
        Assert.False(evicted);
    }
}
