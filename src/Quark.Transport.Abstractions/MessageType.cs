namespace Quark.Transport.Abstractions;

/// <summary>Classifies the kind of a <see cref="MessageEnvelope"/>.</summary>
public enum MessageType : byte
{
    /// <summary>Outbound grain call (expects a response).</summary>
    Request = 0,

    /// <summary>Response to a <see cref="Request"/>.</summary>
    Response = 1,

    /// <summary>One-way grain call (fire-and-forget, no response expected).</summary>
    OneWayRequest = 2,

    /// <summary>Internal system/control message (heartbeat, membership, etc.).</summary>
    System = 3,
}