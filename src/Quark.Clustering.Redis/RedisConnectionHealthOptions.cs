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