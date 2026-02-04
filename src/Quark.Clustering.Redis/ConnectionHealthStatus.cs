namespace Quark.Clustering.Redis;

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