namespace Quark.EventSourcing;

/// <summary>
///     Base class for domain events in event sourcing.
/// </summary>
public abstract class DomainEvent
{
    /// <summary>
    ///     Gets or sets the event identifier.
    /// </summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    ///     Gets or sets the actor identifier that generated this event.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the type of event.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets the sequence number of this event in the actor's event stream.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    ///     Gets or sets optional metadata associated with the event.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
