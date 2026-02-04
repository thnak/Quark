namespace Quark.Transport.Grpc;

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