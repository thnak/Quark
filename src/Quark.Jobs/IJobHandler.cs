namespace Quark.Jobs;

/// <summary>
///     Interface for job handlers that process specific job types.
/// </summary>
/// <typeparam name="TPayload">The type of payload the handler processes.</typeparam>
public interface IJobHandler<TPayload>
{
    /// <summary>
    ///     Executes the job with the given payload.
    /// </summary>
    /// <param name="payload">The job payload.</param>
    /// <param name="context">The job execution context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The result of the job execution (can be null).</returns>
    Task<object?> ExecuteAsync(TPayload payload, JobContext context, CancellationToken cancellationToken = default);
}

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
