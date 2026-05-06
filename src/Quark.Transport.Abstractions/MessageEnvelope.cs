namespace Quark.Transport.Abstractions;

/// <summary>
/// Wire-level envelope wrapping every Quark message.
/// Provides the minimum metadata required to route the message and match responses to requests.
/// </summary>
public sealed class MessageEnvelope
{
    /// <summary>Monotonically increasing request/correlation identifier.</summary>
    public long CorrelationId { get; init; }

    /// <summary>Type of message (request, response, one-way, system).</summary>
    public MessageType MessageType { get; init; }

    /// <summary>
    /// Serialized payload.  The actual type is determined by <see cref="MessageType"/> and
    /// the codec registry.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Optional header values for routing, tracing, and deadlines.</summary>
    public MessageHeaders? Headers { get; init; }
}