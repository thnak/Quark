using System.Net;
using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired on the silo when a TCP client registers an observer back-channel.</summary>
public readonly struct ObserverRegisteredEvent(GrainId observerId, EndPoint? remoteEndPoint)
{
    public GrainId ObserverId { get; } = observerId;
    public EndPoint? RemoteEndPoint { get; } = remoteEndPoint;
}