using System.Net;
using Quark.Core.Abstractions.Identity;
using Quark.Transport.Abstractions;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a new TCP client connection is accepted by the gateway.</summary>
public readonly struct ConnectionAcceptedEvent(string connectionId, EndPoint? remoteEndPoint, int activeConnectionCount)
{
    public string ConnectionId { get; } = connectionId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
    public int ActiveConnectionCount { get; } = activeConnectionCount;
}

/// <summary>Fired when a TCP client connection is closed (gracefully or due to error).</summary>
public readonly struct ConnectionClosedEvent(string connectionId, EndPoint? remoteEndPoint, TimeSpan duration, Exception? error)
{
    public string ConnectionId { get; } = connectionId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
    public TimeSpan Duration { get; } = duration;
    public Exception? Error { get; } = error;
    public bool IsGraceful => Error is null;
}

/// <summary>Fired after a gateway message is dispatched to the local grain invoker.</summary>
public readonly struct MessageDispatchedEvent(string connectionId, GrainId grainId, MessageType messageType, TimeSpan dispatchDuration, bool success)
{
    public string ConnectionId { get; } = connectionId;
    public GrainId GrainId { get; } = grainId;
    public MessageType MessageType { get; } = messageType;
    public TimeSpan DispatchDuration { get; } = dispatchDuration;
    public bool Success { get; } = success;
}
