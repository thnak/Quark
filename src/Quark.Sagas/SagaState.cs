namespace Quark.Sagas;

/// <summary>
/// Represents the persistent state of a saga execution.
/// </summary>
public sealed class SagaState
{
    /// <summary>
    /// Gets or sets the unique identifier of the saga instance.
    /// </summary>
    public required string SagaId { get; init; }

    /// <summary>
    /// Gets or sets the current status of the saga.
    /// </summary>
    public SagaStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the index of the currently executing step.
    /// </summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>
    /// Gets or sets the list of completed step names (for audit trail).
    /// </summary>
    public List<string> CompletedSteps { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of compensated step names (for audit trail).
    /// </summary>
    public List<string> CompensatedSteps { get; set; } = new();

    /// <summary>
    /// Gets or sets the exception message if the saga failed.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the saga started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the saga completed or was compensated.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets custom metadata for the saga.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
