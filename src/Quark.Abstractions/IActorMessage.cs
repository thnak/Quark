namespace Quark.Abstractions;

/// <summary>
///     Represents a message to be processed by an actor.
/// </summary>
public interface IActorMessage
{
    /// <summary>
    ///     Gets the unique identifier for this message.
    /// </summary>
    string MessageId { get; }

    /// <summary>
    ///     Gets the correlation ID for distributed tracing.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    ///     Gets the timestamp when the message was created.
    /// </summary>
    DateTimeOffset Timestamp { get; }
}