using Quark.Transport.Grpc;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for gRPC channel pooling and lifecycle management.
/// </summary>
public sealed class GrpcChannelPoolTests : IDisposable
{
    private readonly GrpcChannelPool _pool;

    public GrpcChannelPoolTests()
    {
        _pool = new GrpcChannelPool(new GrpcChannelPoolOptions
        {
            MaxChannelLifetime = TimeSpan.FromSeconds(5),
            HealthCheckInterval = TimeSpan.FromSeconds(1),
            DisposeIdleChannels = true,
            IdleTimeout = TimeSpan.FromSeconds(3)
        });
    }

    [Fact]
    public void GetOrCreateChannel_CreatesNewChannel_WhenNotExists()
    {
        // Arrange
        var endpoint = "http://localhost:5000";

        // Act
        var channel = _pool.GetOrCreateChannel(endpoint);

        // Assert
        Assert.NotNull(channel);
        var stats = _pool.GetStats();
        Assert.Equal(1, stats.TotalChannels);
    }

    [Fact]
    public void GetOrCreateChannel_ReusesExistingChannel_WhenExists()
    {
        // Arrange
        var endpoint = "http://localhost:5000";
        var firstChannel = _pool.GetOrCreateChannel(endpoint);

        // Act
        var secondChannel = _pool.GetOrCreateChannel(endpoint);

        // Assert
        Assert.Same(firstChannel, secondChannel);
        var stats = _pool.GetStats();
        Assert.Equal(1, stats.TotalChannels);
    }

    [Fact]
    public void GetOrCreateChannel_CreatesDifferentChannels_ForDifferentEndpoints()
    {
        // Arrange
        var endpoint1 = "http://localhost:5000";
        var endpoint2 = "http://localhost:5001";

        // Act
        var channel1 = _pool.GetOrCreateChannel(endpoint1);
        var channel2 = _pool.GetOrCreateChannel(endpoint2);

        // Assert
        Assert.NotSame(channel1, channel2);
        var stats = _pool.GetStats();
        Assert.Equal(2, stats.TotalChannels);
    }

    [Fact]
    public void RemoveChannel_DisposesAndRemovesChannel()
    {
        // Arrange
        var endpoint = "http://localhost:5000";
        _pool.GetOrCreateChannel(endpoint);

        // Act
        _pool.RemoveChannel(endpoint);

        // Assert
        var stats = _pool.GetStats();
        Assert.Equal(0, stats.TotalChannels);
    }

    [Fact]
    public void GetChannelState_ReturnsNull_WhenChannelDoesNotExist()
    {
        // Arrange
        var endpoint = "http://localhost:5000";

        // Act
        var state = _pool.GetChannelState(endpoint);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public void GetChannelState_ReturnsState_WhenChannelExists()
    {
        // Arrange
        var endpoint = "http://localhost:5000";
        _pool.GetOrCreateChannel(endpoint);

        // Act
        var state = _pool.GetChannelState(endpoint);

        // Assert
        Assert.NotNull(state);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStats_ForMultipleChannels()
    {
        // Arrange
        _pool.GetOrCreateChannel("http://localhost:5000");
        _pool.GetOrCreateChannel("http://localhost:5001");
        _pool.GetOrCreateChannel("http://localhost:5002");

        // Act
        var stats = _pool.GetStats();

        // Assert
        Assert.Equal(3, stats.TotalChannels);
        Assert.True(stats.OldestChannelAge < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HealthCheckTimer_RecyclesOldChannels_AfterMaxLifetime()
    {
        // Arrange
        var poolWithShortLifetime = new GrpcChannelPool(new GrpcChannelPoolOptions
        {
            MaxChannelLifetime = TimeSpan.FromMilliseconds(100),
            HealthCheckInterval = TimeSpan.FromMilliseconds(50),
            DisposeIdleChannels = false
        });

        var endpoint = "http://localhost:5000";
        poolWithShortLifetime.GetOrCreateChannel(endpoint);

        // Act - wait for health check to recycle the channel
        await Task.Delay(300);

        // Assert - channel should have been removed
        var stats = poolWithShortLifetime.GetStats();
        Assert.Equal(0, stats.TotalChannels);

        poolWithShortLifetime.Dispose();
    }

    [Fact]
    public async Task HealthCheckTimer_DisposesIdleChannels_AfterIdleTimeout()
    {
        // Arrange
        var poolWithShortIdle = new GrpcChannelPool(new GrpcChannelPoolOptions
        {
            MaxChannelLifetime = null, // Disable lifetime recycling
            HealthCheckInterval = TimeSpan.FromMilliseconds(50),
            DisposeIdleChannels = true,
            IdleTimeout = TimeSpan.FromMilliseconds(100)
        });

        var endpoint = "http://localhost:5000";
        poolWithShortIdle.GetOrCreateChannel(endpoint);

        // Act - wait for health check to dispose idle channel
        await Task.Delay(300);

        // Assert - channel should have been removed due to idleness
        var stats = poolWithShortIdle.GetStats();
        Assert.Equal(0, stats.TotalChannels);

        poolWithShortIdle.Dispose();
    }

    [Fact]
    public void GetOrCreateChannel_ThrowsException_ForNullOrEmptyEndpoint()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => _pool.GetOrCreateChannel(null!));
        Assert.Throws<ArgumentException>(() => _pool.GetOrCreateChannel(""));
        Assert.Throws<ArgumentException>(() => _pool.GetOrCreateChannel("   "));
    }

    [Fact]
    public void GetOrCreateChannel_ThrowsObjectDisposedException_AfterDispose()
    {
        // Arrange
        var pool = new GrpcChannelPool();
        pool.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => pool.GetOrCreateChannel("http://localhost:5000"));
    }

    public void Dispose()
    {
        _pool.Dispose();
    }
}
