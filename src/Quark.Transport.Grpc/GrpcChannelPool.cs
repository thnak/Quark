using System.Collections.Concurrent;
using Grpc.Net.Client;
using ConnectivityState = Grpc.Core.ConnectivityState;

namespace Quark.Transport.Grpc;

/// <summary>
/// Options for configuring gRPC channel pooling and lifecycle management.
/// </summary>
public sealed class GrpcChannelPoolOptions
{
    /// <summary>
    /// Gets or sets the maximum lifetime of a channel before it should be recycled.
    /// Defaults to 30 minutes. Set to null to disable automatic recycling.
    /// </summary>
    public TimeSpan? MaxChannelLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the interval for checking channel health.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether to automatically dispose idle channels.
    /// Defaults to true.
    /// </summary>
    public bool DisposeIdleChannels { get; set; } = true;

    /// <summary>
    /// Gets or sets the idle timeout before a channel is disposed.
    /// Defaults to 10 minutes. Only applies if DisposeIdleChannels is true.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Manages a pool of gRPC channels with lifecycle management, health monitoring, and automatic recycling.
/// Prevents duplicate connections and enables efficient resource sharing.
/// </summary>
public sealed class GrpcChannelPool : IDisposable
{
    private readonly ConcurrentDictionary<string, ChannelEntry> _channels = new();
    private readonly GrpcChannelPoolOptions _options;
    private readonly Timer? _healthCheckTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcChannelPool"/> class.
    /// </summary>
    /// <param name="options">Optional configuration options for the channel pool.</param>
    public GrpcChannelPool(GrpcChannelPoolOptions? options = null)
    {
        _options = options ?? new GrpcChannelPoolOptions();
        
        if (_options.HealthCheckInterval > TimeSpan.Zero)
        {
            _healthCheckTimer = new Timer(
                HealthCheckCallback,
                null,
                _options.HealthCheckInterval,
                _options.HealthCheckInterval);
        }
    }

    /// <summary>
    /// Gets or creates a gRPC channel for the specified endpoint.
    /// Reuses existing channels when possible to avoid duplicate connections.
    /// </summary>
    /// <param name="endpoint">The endpoint address (e.g., "http://localhost:5000").</param>
    /// <param name="configure">Optional action to configure channel options.</param>
    /// <returns>A gRPC channel for the endpoint.</returns>
    public GrpcChannel GetOrCreateChannel(string endpoint, Action<GrpcChannelOptions>? configure = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GrpcChannelPool));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        return _channels.AddOrUpdate(
            endpoint,
            // Add factory: create new channel
            key =>
            {
                var channelOptions = new GrpcChannelOptions();
                configure?.Invoke(channelOptions);
                
                var channel = GrpcChannel.ForAddress(key, channelOptions);
                return new ChannelEntry(channel, DateTimeOffset.UtcNow);
            },
            // Update factory: reuse or recycle
            (key, existingEntry) =>
            {
                // Check if channel needs recycling
                if (ShouldRecycleChannel(existingEntry))
                {
                    // Dispose old channel
                    existingEntry.Channel.Dispose();
                    
                    // Create new channel
                    var channelOptions = new GrpcChannelOptions();
                    configure?.Invoke(channelOptions);
                    
                    var channel = GrpcChannel.ForAddress(key, channelOptions);
                    return new ChannelEntry(channel, DateTimeOffset.UtcNow);
                }
                else
                {
                    // Reuse existing channel
                    existingEntry.UpdateLastAccessed();
                    return existingEntry;
                }
            }).Channel;
    }

    /// <summary>
    /// Removes a channel from the pool and disposes it.
    /// </summary>
    /// <param name="endpoint">The endpoint address of the channel to remove.</param>
    public void RemoveChannel(string endpoint)
    {
        if (_channels.TryRemove(endpoint, out var entry))
        {
            entry.Channel.Dispose();
        }
    }

    /// <summary>
    /// Gets the current state of a channel.
    /// </summary>
    /// <param name="endpoint">The endpoint address.</param>
    /// <returns>The channel state, or null if the channel is not in the pool.</returns>
    public ConnectivityState? GetChannelState(string endpoint)
    {
        if (_channels.TryGetValue(endpoint, out var entry))
        {
            return entry.Channel.State;
        }
        return null;
    }

    /// <summary>
    /// Gets statistics about the channel pool.
    /// </summary>
    public ChannelPoolStats GetStats()
    {
        var now = DateTimeOffset.UtcNow;
        var activeCount = 0;
        var idleCount = 0;
        var oldestChannelAge = TimeSpan.Zero;

        foreach (var entry in _channels.Values)
        {
            var age = now - entry.CreatedAt;
            if (age > oldestChannelAge)
            {
                oldestChannelAge = age;
            }

            var idleTime = now - entry.LastAccessedAt;
            if (idleTime > _options.IdleTimeout)
            {
                idleCount++;
            }
            else
            {
                activeCount++;
            }
        }

        return new ChannelPoolStats(
            TotalChannels: _channels.Count,
            ActiveChannels: activeCount,
            IdleChannels: idleCount,
            OldestChannelAge: oldestChannelAge);
    }

    private bool ShouldRecycleChannel(ChannelEntry entry)
    {
        if (_options.MaxChannelLifetime == null)
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - entry.CreatedAt;
        return age > _options.MaxChannelLifetime.Value;
    }

    private void HealthCheckCallback(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var endpointsToRemove = new List<string>();

        foreach (var kvp in _channels)
        {
            var endpoint = kvp.Key;
            var entry = kvp.Value;

            // Check if channel should be recycled due to age
            if (ShouldRecycleChannel(entry))
            {
                endpointsToRemove.Add(endpoint);
                continue;
            }

            // Check if channel is idle and should be disposed
            if (_options.DisposeIdleChannels)
            {
                var idleTime = now - entry.LastAccessedAt;
                if (idleTime > _options.IdleTimeout)
                {
                    endpointsToRemove.Add(endpoint);
                    continue;
                }
            }

            // Check channel state
            var channelState = entry.Channel.State;
            if (channelState == ConnectivityState.TransientFailure || 
                channelState == ConnectivityState.Shutdown)
            {
                endpointsToRemove.Add(endpoint);
            }
        }

        // Remove unhealthy or idle channels
        foreach (var endpoint in endpointsToRemove)
        {
            RemoveChannel(endpoint);
        }
    }

    /// <summary>
    /// Disposes all channels in the pool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthCheckTimer?.Dispose();

        foreach (var entry in _channels.Values)
        {
            entry.Channel.Dispose();
        }

        _channels.Clear();
    }

    private sealed class ChannelEntry
    {
        private long _lastAccessedTicks;

        public ChannelEntry(GrpcChannel channel, DateTimeOffset createdAt)
        {
            Channel = channel;
            CreatedAt = createdAt;
            _lastAccessedTicks = createdAt.UtcTicks;
        }

        public GrpcChannel Channel { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset LastAccessedAt => new DateTimeOffset(_lastAccessedTicks, TimeSpan.Zero);

        public void UpdateLastAccessed()
        {
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        }
    }
}

/// <summary>
/// Statistics about a gRPC channel pool.
/// </summary>
/// <param name="TotalChannels">Total number of channels in the pool.</param>
/// <param name="ActiveChannels">Number of recently accessed channels.</param>
/// <param name="IdleChannels">Number of idle channels.</param>
/// <param name="OldestChannelAge">Age of the oldest channel in the pool.</param>
public record ChannelPoolStats(
    int TotalChannels,
    int ActiveChannels,
    int IdleChannels,
    TimeSpan OldestChannelAge);
