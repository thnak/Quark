using System.Net;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a new TCP client connection is accepted by the gateway.</summary>
public readonly struct ConnectionAcceptedEvent(string connectionId, EndPoint? remoteEndPoint, int activeConnectionCount)
{
    public string ConnectionId { get; } = connectionId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
    public int ActiveConnectionCount { get; } = activeConnectionCount;
}