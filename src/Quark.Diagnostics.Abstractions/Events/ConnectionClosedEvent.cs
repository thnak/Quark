using System.Net;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a TCP client connection is closed (gracefully or due to error).</summary>
public readonly struct ConnectionClosedEvent(string connectionId, EndPoint? remoteEndPoint, TimeSpan duration, Exception? error)
{
    public string ConnectionId { get; } = connectionId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
    public TimeSpan Duration { get; } = duration;
    public Exception? Error { get; } = error;
    public bool IsGraceful => Error is null;
}