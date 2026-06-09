using System.Net;
using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired on the silo when a TCP client registers an observer back-channel.</summary>
public readonly struct ObserverRegisteredEvent(GrainId observerId, EndPoint? remoteEndPoint)
{
    public GrainId ObserverId { get; } = observerId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
}

/// <summary>Fired on the silo when an observer back-channel entry is removed (connection closed).</summary>
public readonly struct ObserverDeregisteredEvent(GrainId observerId)
{
    public GrainId ObserverId { get; } = observerId;
}

/// <summary>Fired on the silo after an observer method invocation is dispatched (success or error).</summary>
public readonly struct ObserverInvokedEvent(GrainId observerId, uint methodId, bool success, Exception? exception)
{
    public GrainId ObserverId { get; } = observerId;
    public uint MethodId { get; } = methodId;
    public bool Success { get; } = success;
    public Exception? Exception { get; } = exception;
}
