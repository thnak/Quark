namespace Quark.Networking.Abstractions;

/// <summary>
///     Represents a universal message envelope for actor invocations in the Quark cluster.
///     All actor calls are wrapped in this envelope for transport across silos.
/// </summary>
public sealed class QuarkEnvelope
{
    public bool IsResponse { get; set; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="QuarkEnvelope" /> class.
    /// </summary>
    public QuarkEnvelope(
        string messageId,
        string actorId,
        string actorType,
        string methodName,
        byte[] payload,
        string? correlationId = null,
        bool isResponse = false)
    {
        IsResponse = isResponse;
        MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Gets the unique message identifier.
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    ///     Gets the target actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    ///     Gets the target actor type name.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    ///     Gets the method name to invoke.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    ///     Gets the serialized payload (method arguments).
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    ///     Gets the correlation ID for distributed tracing.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    ///     Gets the timestamp when the message was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     Gets or sets the response payload (for response envelopes).
    /// </summary>
    public byte[]? ResponsePayload { get; set; }

    /// <summary>
    ///     Gets or sets whether this envelope represents an error.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    ///     Gets or sets the error message (if IsError is true).
    /// </summary>
    public string? ErrorMessage { get; set; }
}