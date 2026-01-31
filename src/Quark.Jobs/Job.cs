namespace Quark.Jobs;

/// <summary>
///     Represents a durable job with persistent state.
/// </summary>
public sealed class Job
{
    /// <summary>
    ///     Gets or sets the unique identifier for this job.
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    ///     Gets or sets the job type identifier (used for dispatching).
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    ///     Gets or sets the serialized payload for the job.
    /// </summary>
    public required byte[] Payload { get; set; }

    /// <summary>
    ///     Gets or sets the job priority (higher values = higher priority).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Gets or sets when the job should be executed (null for immediate).
    /// </summary>
    public DateTimeOffset? ScheduledAt { get; set; }

    /// <summary>
    ///     Gets or sets the maximum time allowed for job execution.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    ///     Gets or sets the retry policy for this job.
    /// </summary>
    public required RetryPolicy RetryPolicy { get; set; }

    /// <summary>
    ///     Gets or sets the job dependencies (jobs that must complete first).
    /// </summary>
    public JobDependencies? Dependencies { get; set; }

    /// <summary>
    ///     Gets or sets the current status of the job.
    /// </summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    ///     Gets or sets when the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets when the job was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets or sets when the job execution started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    ///     Gets or sets when the job execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    ///     Gets or sets the number of times this job has been attempted.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     Gets or sets the last error message (if failed).
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     Gets or sets the serialized result of the job (if successful).
    /// </summary>
    public byte[]? Result { get; set; }

    /// <summary>
    ///     Gets or sets the current progress percentage (0-100).
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    ///     Gets or sets custom metadata for the job.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
