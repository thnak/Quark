using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Quark.Clustering.Redis;

/// <summary>
/// Options for configuring Redis connection health monitoring.
/// </summary>
public sealed class RedisConnectionHealthOptions
{
    /// <summary>
    /// Gets or sets the interval for checking connection health.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to enable automatic reconnection.
    /// Defaults to true.
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout for connection health checks.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets whether to monitor connection failures.
    /// Defaults to true.
    /// </summary>
    public bool MonitorConnectionFailures { get; set; } = true;
}

/// <summary>
/// Monitors Redis connection health and provides automatic recovery mechanisms.
/// </summary>
public sealed class RedisConnectionHealthMonitor : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisConnectionHealthOptions _options;
    private readonly ILogger<RedisConnectionHealthMonitor>? _logger;
    private readonly Timer? _healthCheckTimer;
    private bool _disposed;
    private int _failureCount;
    private DateTimeOffset _lastSuccessfulCheck;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisConnectionHealthMonitor"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection to monitor.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public RedisConnectionHealthMonitor(
        IConnectionMultiplexer redis,
        RedisConnectionHealthOptions? options = null,
        ILogger<RedisConnectionHealthMonitor>? logger = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? new RedisConnectionHealthOptions();
        _logger = logger;
        _lastSuccessfulCheck = DateTimeOffset.UtcNow;

        // Subscribe to connection events
        if (_options.MonitorConnectionFailures)
        {
            _redis.ConnectionFailed += OnConnectionFailed;
            _redis.ConnectionRestored += OnConnectionRestored;
            _redis.ErrorMessage += OnErrorMessage;
        }

        // Start health check timer
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
    /// Event raised when connection health degrades.
    /// </summary>
    public event EventHandler<ConnectionHealthDegradedEventArgs>? ConnectionHealthDegraded;

    /// <summary>
    /// Event raised when connection is restored after failure.
    /// </summary>
    public event EventHandler<ConnectionRestoredEventArgs>? ConnectionRestored;

    /// <summary>
    /// Gets the current health status of the Redis connection.
    /// </summary>
    public async Task<ConnectionHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var startTime = DateTimeOffset.UtcNow;
            
            // Perform a simple ping to check connectivity
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.HealthCheckTimeout);
            
            var latency = await db.PingAsync();
            var duration = DateTimeOffset.UtcNow - startTime;

            _lastSuccessfulCheck = DateTimeOffset.UtcNow;
            _failureCount = 0;

            return new ConnectionHealthStatus(
                IsHealthy: true,
                IsConnected: _redis.IsConnected,
                LatencyMs: latency.TotalMilliseconds,
                FailureCount: _failureCount,
                LastSuccessfulCheck: _lastSuccessfulCheck,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _failureCount++;
            
            _logger?.LogWarning(ex, 
                "Redis connection health check failed (failure #{FailureCount})", 
                _failureCount);

            return new ConnectionHealthStatus(
                IsHealthy: false,
                IsConnected: _redis.IsConnected,
                LatencyMs: null,
                FailureCount: _failureCount,
                LastSuccessfulCheck: _lastSuccessfulCheck,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Attempts to recover the connection if it's in a failed state.
    /// </summary>
    public async Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAutoReconnect)
        {
            return false;
        }

        try
        {
            _logger?.LogInformation("Attempting to recover Redis connection...");

            // Check if connection can be established
            var status = await GetHealthStatusAsync(cancellationToken);
            
            if (status.IsHealthy)
            {
                _logger?.LogInformation("Redis connection recovered successfully");
                return true;
            }

            // If not connected, try to reconnect by accessing a database
            // StackExchange.Redis will automatically attempt to reconnect
            var db = _redis.GetDatabase();
            await db.PingAsync();

            _logger?.LogInformation("Redis connection recovery successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to recover Redis connection");
            return false;
        }
    }

    private void HealthCheckCallback(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var status = await GetHealthStatusAsync();
            
            if (!status.IsHealthy && _failureCount > 0)
            {
                ConnectionHealthDegraded?.Invoke(this, new ConnectionHealthDegradedEventArgs(status));

                if (_options.EnableAutoReconnect)
                {
                    await TryRecoverAsync();
                }
            }
        });
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        _failureCount++;
        _logger?.LogWarning(
            "Redis connection failed: {FailureType} on {EndPoint}. Message: {Message}",
            e.FailureType,
            e.EndPoint,
            e.Exception?.Message ?? "Unknown");
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        var previousFailureCount = _failureCount;
        _failureCount = 0;
        _lastSuccessfulCheck = DateTimeOffset.UtcNow;
        
        _logger?.LogInformation(
            "Redis connection restored on {EndPoint} after {FailureCount} failures",
            e.EndPoint,
            previousFailureCount);

        ConnectionRestored?.Invoke(this, new ConnectionRestoredEventArgs(e.EndPoint, previousFailureCount));
    }

    private void OnErrorMessage(object? sender, RedisErrorEventArgs e)
    {
        _logger?.LogWarning("Redis error on {EndPoint}: {Message}", e.EndPoint, e.Message);
    }

    /// <summary>
    /// Disposes the health monitor and unsubscribes from connection events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthCheckTimer?.Dispose();

        if (_options.MonitorConnectionFailures)
        {
            _redis.ConnectionFailed -= OnConnectionFailed;
            _redis.ConnectionRestored -= OnConnectionRestored;
            _redis.ErrorMessage -= OnErrorMessage;
        }
    }
}

/// <summary>
/// Represents the health status of a Redis connection.
/// </summary>
/// <param name="IsHealthy">Whether the connection is healthy.</param>
/// <param name="IsConnected">Whether the connection is currently connected.</param>
/// <param name="LatencyMs">Latency in milliseconds, or null if check failed.</param>
/// <param name="FailureCount">Number of consecutive failures.</param>
/// <param name="LastSuccessfulCheck">Timestamp of the last successful health check.</param>
/// <param name="ErrorMessage">Error message if the connection is unhealthy.</param>
public record ConnectionHealthStatus(
    bool IsHealthy,
    bool IsConnected,
    double? LatencyMs,
    int FailureCount,
    DateTimeOffset LastSuccessfulCheck,
    string? ErrorMessage);

/// <summary>
/// Event arguments for connection health degradation.
/// </summary>
public sealed class ConnectionHealthDegradedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHealthDegradedEventArgs"/> class.
    /// </summary>
    public ConnectionHealthDegradedEventArgs(ConnectionHealthStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the current health status.
    /// </summary>
    public ConnectionHealthStatus Status { get; }
}

/// <summary>
/// Event arguments for connection restoration.
/// </summary>
public sealed class ConnectionRestoredEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionRestoredEventArgs"/> class.
    /// </summary>
    public ConnectionRestoredEventArgs(System.Net.EndPoint? endPoint, int previousFailureCount)
    {
        EndPoint = endPoint;
        PreviousFailureCount = previousFailureCount;
    }

    /// <summary>
    /// Gets the endpoint that was restored.
    /// </summary>
    public System.Net.EndPoint? EndPoint { get; }

    /// <summary>
    /// Gets the number of failures before restoration.
    /// </summary>
    public int PreviousFailureCount { get; }
}
