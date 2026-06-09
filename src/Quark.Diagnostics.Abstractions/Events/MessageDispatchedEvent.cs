using Quark.Core.Abstractions.Identity;
using Quark.Transport.Abstractions;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired after a gateway message is dispatched to the local grain invoker.</summary>
public readonly struct MessageDispatchedEvent(string connectionId, GrainId grainId, MessageType messageType, TimeSpan dispatchDuration, bool success)
{
    public string ConnectionId { get; } = connectionId;
    public GrainId GrainId { get; } = grainId;
    public MessageType MessageType { get; } = messageType;
    public TimeSpan DispatchDuration { get; } = dispatchDuration;
    public bool Success { get; } = success;
}