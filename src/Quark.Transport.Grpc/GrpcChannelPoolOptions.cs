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