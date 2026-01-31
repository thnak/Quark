namespace Quark.DurableTasks;

/// <summary>
///     Represents the state of an orchestration instance.
/// </summary>
public sealed class OrchestrationState
{
    /// <summary>
    ///     Gets or sets the unique identifier for this orchestration instance.
    /// </summary>
    public required string OrchestrationId { get; set; }

    /// <summary>
    ///     Gets or sets the orchestration name/type.
    /// </summary>
    public required string OrchestrationName { get; set; }

    /// <summary>
    ///     Gets or sets the serialized input for the orchestration.
    /// </summary>
    public required byte[] Input { get; set; }

    /// <summary>
    ///     Gets or sets the current status of the orchestration.
    /// </summary>
    public OrchestrationStatus Status { get; set; } = OrchestrationStatus.Running;

    /// <summary>
    ///     Gets or sets when the orchestration was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets when the orchestration was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets when the orchestration completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    ///     Gets or sets the serialized output/result (if completed successfully).
    /// </summary>
    public byte[]? Output { get; set; }

    /// <summary>
    ///     Gets or sets the error message (if failed).
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    ///     Gets or sets the orchestration history (list of events).
    /// </summary>
    public List<OrchestrationEvent> History { get; set; } = new();

    /// <summary>
    ///     Gets or sets the current checkpoint position in the history.
    /// </summary>
    public int CurrentEventIndex { get; set; }

    /// <summary>
    ///     Gets or sets pending external events the orchestration is waiting for.
    /// </summary>
    public Dictionary<string, byte[]> PendingExternalEvents { get; set; } = new();
}

/// <summary>
///     Represents the status of an orchestration.
/// </summary>
public enum OrchestrationStatus
{
    /// <summary>
    ///     Orchestration is currently running.
    /// </summary>
    Running = 0,

    /// <summary>
    ///     Orchestration completed successfully.
    /// </summary>
    Completed = 1,

    /// <summary>
    ///     Orchestration failed.
    /// </summary>
    Failed = 2,

    /// <summary>
    ///     Orchestration was cancelled.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    ///     Orchestration is waiting for an external event.
    /// </summary>
    Suspended = 4
}
