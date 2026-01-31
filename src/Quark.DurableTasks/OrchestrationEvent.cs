namespace Quark.DurableTasks;

/// <summary>
///     Represents an event in the orchestration history.
/// </summary>
public sealed class OrchestrationEvent
{
    /// <summary>
    ///     Gets or sets the event ID (sequence number).
    /// </summary>
    public int EventId { get; set; }

    /// <summary>
    ///     Gets or sets the type of event.
    /// </summary>
    public required OrchestrationEventType EventType { get; set; }

    /// <summary>
    ///     Gets or sets when the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets the activity name (for activity events).
    /// </summary>
    public string? ActivityName { get; set; }

    /// <summary>
    ///     Gets or sets the serialized input/output/event data.
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    ///     Gets or sets the error message (if the event represents a failure).
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
///     Types of orchestration events.
/// </summary>
public enum OrchestrationEventType
{
    /// <summary>
    ///     Orchestration started.
    /// </summary>
    OrchestrationStarted = 0,

    /// <summary>
    ///     Orchestration completed.
    /// </summary>
    OrchestrationCompleted = 1,

    /// <summary>
    ///     Orchestration failed.
    /// </summary>
    OrchestrationFailed = 2,

    /// <summary>
    ///     Activity scheduled for execution.
    /// </summary>
    ActivityScheduled = 3,

    /// <summary>
    ///     Activity completed successfully.
    /// </summary>
    ActivityCompleted = 4,

    /// <summary>
    ///     Activity failed.
    /// </summary>
    ActivityFailed = 5,

    /// <summary>
    ///     Timer created.
    /// </summary>
    TimerCreated = 6,

    /// <summary>
    ///     Timer fired.
    /// </summary>
    TimerFired = 7,

    /// <summary>
    ///     External event received.
    /// </summary>
    ExternalEventReceived = 8,

    /// <summary>
    ///     Sub-orchestration scheduled.
    /// </summary>
    SubOrchestrationScheduled = 9,

    /// <summary>
    ///     Sub-orchestration completed.
    /// </summary>
    SubOrchestrationCompleted = 10,

    /// <summary>
    ///     Sub-orchestration failed.
    /// </summary>
    SubOrchestrationFailed = 11
}
