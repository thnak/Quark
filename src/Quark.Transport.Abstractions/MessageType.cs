namespace Quark.Transport.Abstractions;

/// <summary>Classifies the kind of a <see cref="MessageEnvelope" />.</summary>
public enum MessageType : byte
{
    /// <summary>Outbound grain call (expects a response).</summary>
    Request = 0,

    /// <summary>Response to a <see cref="Request" />.</summary>
    Response = 1,

    /// <summary>One-way grain call (fire-and-forget, no response expected).</summary>
    OneWayRequest = 2,

    /// <summary>Internal system/control message (heartbeat, membership, etc.).</summary>
    System = 3,

    /// <summary>Subscribe to a grain stream (client → server).</summary>
    StreamSubscribe = 4,

    /// <summary>Unsubscribe from a grain stream (client → server, one-way).</summary>
    StreamUnsubscribe = 5,

    /// <summary>Push stream data (server → client, unsolicited).</summary>
    StreamPush = 6,

    /// <summary>Client registers a local observer GrainId with the silo (client → server, one-way).</summary>
    ObserverRegister = 7,

    /// <summary>Silo invokes a method on a client-side observer (server → client, one-way).</summary>
    ObserverInvoke = 8,

    /// <summary>Client unregisters a local observer GrainId from the silo (client → server, one-way).</summary>
    ObserverUnregister = 9,

    /// <summary>
    ///     Silo-to-silo control frame: instructs the receiving silo to deactivate the named grain
    ///     (one-way, no response).  Handled inline on the read loop, not via the grain invoker.
    /// </summary>
    TerminateRequest = 10,
}
