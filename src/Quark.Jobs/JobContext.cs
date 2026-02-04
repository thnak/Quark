namespace Quark.Jobs;

/// <summary>
///     Context information available during job execution.
/// </summary>
public sealed class JobContext
{
    /// <summary>
    ///     Gets the job identifier.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    ///     Gets the job type.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    ///     Gets the current attempt number (1-based).
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    ///     Gets custom metadata for the job.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    ///     Gets a callback to update job progress.
    /// </summary>
    public required Func<int, Task> UpdateProgress { get; init; }
}