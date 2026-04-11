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

/// <summary>
/// Key/value headers attached to a <see cref="MessageEnvelope"/>.
/// All keys and values are strings to keep encoding trivial and AOT-safe.
/// </summary>
public sealed class MessageHeaders
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Sets a header value.</summary>
    public void Set(string key, string value) => _values[key] = value;

    /// <summary>Gets a header value, or <c>null</c> if not present.</summary>
    public string? Get(string key) => _values.TryGetValue(key, out string? v) ? v : null;

    /// <summary>All headers as a read-only view.</summary>
    public IReadOnlyDictionary<string, string> All => _values;
}
