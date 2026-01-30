using Quark.Clustering.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for Redis connection health monitoring and recovery.
/// </summary>
public sealed class RedisConnectionHealthMonitorTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private RedisConnectionHealthMonitor? _monitor;

    public async Task InitializeAsync()
    {
        // Start Redis container
        _redisContainer = new RedisBuilder("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // Connect to Redis
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());

        // Create monitor
        _monitor = new RedisConnectionHealthMonitor(_redis, new RedisConnectionHealthOptions
        {
            HealthCheckInterval = TimeSpan.FromMilliseconds(100),
            EnableAutoReconnect = true,
            HealthCheckTimeout = TimeSpan.FromSeconds(5),
            MonitorConnectionFailures = true
        });
    }

    public async Task DisposeAsync()
    {
        _monitor?.Dispose();
        _redis?.Dispose();

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetHealthStatusAsync_ReturnsHealthy_WhenConnectionIsGood()
    {
        // Act
        var status = await _monitor!.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.True(status.IsHealthy);
        Assert.True(status.IsConnected);
        Assert.NotNull(status.LatencyMs);
        Assert.True(status.LatencyMs >= 0);
        Assert.Equal(0, status.FailureCount);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task GetHealthStatusAsync_HasLowLatency_ForLocalRedis()
    {
        // Act
        var status = await _monitor!.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(status.LatencyMs);
        Assert.True(status.LatencyMs < 100, $"Latency was {status.LatencyMs}ms, expected < 100ms");
    }

    [Fact]
    public async Task GetHealthStatusAsync_UpdatesLastSuccessfulCheck()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        await Task.Delay(10); // Small delay to ensure time difference
        var status = await _monitor!.GetHealthStatusAsync();

        // Assert
        Assert.True(status.LastSuccessfulCheck >= before);
        Assert.True(status.LastSuccessfulCheck <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TryRecoverAsync_ReturnsTrue_WhenConnectionIsHealthy()
    {
        // Act
        var recovered = await _monitor!.TryRecoverAsync();

        // Assert
        Assert.True(recovered);
    }

    [Fact]
    public async Task ConnectionHealthDegradedEvent_NotRaised_WhenHealthy()
    {
        // Arrange
        var eventRaised = false;
        _monitor!.ConnectionHealthDegraded += (s, e) => eventRaised = true;

        // Act
        var status = await _monitor.GetHealthStatusAsync();

        // Assert
        Assert.False(eventRaised);
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public async Task GetHealthStatusAsync_HandlesMultipleConcurrentRequests()
    {
        // Arrange
        var tasks = new List<Task<ConnectionHealthStatus>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_monitor!.GetHealthStatusAsync());
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, results.Length);
        Assert.All(results, status =>
        {
            Assert.NotNull(status);
            Assert.True(status.IsHealthy);
        });
    }

    [Fact]
    public async Task Constructor_WithNullOptions_UsesDefaultOptions()
    {
        // Arrange & Act
        using var monitor = new RedisConnectionHealthMonitor(_redis!, null);
        var status = await monitor.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenRedisIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RedisConnectionHealthMonitor(null!));
    }

    [Fact]
    public async Task MultipleHealthChecks_MaintainZeroFailureCount_WhenHealthy()
    {
        // Act
        var status1 = await _monitor!.GetHealthStatusAsync();
        await Task.Delay(50);
        var status2 = await _monitor.GetHealthStatusAsync();
        await Task.Delay(50);
        var status3 = await _monitor.GetHealthStatusAsync();

        // Assert
        Assert.Equal(0, status1.FailureCount);
        Assert.Equal(0, status2.FailureCount);
        Assert.Equal(0, status3.FailureCount);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ReportsLatency_InMilliseconds()
    {
        // Act
        var status = await _monitor!.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(status.LatencyMs);
        Assert.True(status.LatencyMs >= 0);
        // Reasonable upper bound for local Redis
        Assert.True(status.LatencyMs < 1000, $"Unexpected high latency: {status.LatencyMs}ms");
    }
}
