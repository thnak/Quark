namespace Quark.Abstractions;

/// <summary>
///     Represents a message stored in the outbox for reliable delivery.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>
    ///     Gets or sets the unique identifier for this message.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    ///     Gets or sets the actor ID that owns this message.
    /// </summary>
    public required string ActorId { get; set; }

    /// <summary>
    ///     Gets or sets the destination actor or service for this message.
    /// </summary>
    public required string Destination { get; set; }

    /// <summary>
    ///     Gets or sets the message type identifier (for deserialization).
    /// </summary>
    public required string MessageType { get; set; }

    /// <summary>
    ///     Gets or sets the serialized payload of the message.
    /// </summary>
    public required byte[] Payload { get; set; }

    /// <summary>
    ///     Gets or sets when the message was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets when the message was successfully sent (null if not sent yet).
    /// </summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>
    ///     Gets or sets the number of times delivery has been attempted.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    ///     Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the last error message if delivery failed.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     Gets or sets when the next retry should be attempted.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; set; }
}
