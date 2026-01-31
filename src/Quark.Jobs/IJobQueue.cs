namespace Quark.Jobs;

/// <summary>
///     Interface for the job queue - manages persistent job storage and retrieval.
/// </summary>
public interface IJobQueue
{
    /// <summary>
    ///     Enqueues a new job for execution.
    /// </summary>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The job ID.</returns>
    Task<string> EnqueueAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Dequeues the next available job for execution.
    ///     Returns null if no jobs are available.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The next job to execute, or null if none available.</returns>
    Task<Job?> DequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a job as completed successfully.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="result">Optional serialized result.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task CompleteAsync(string jobId, byte[]? result = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a job as failed.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task FailAsync(string jobId, Exception exception, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current status of a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The job details, or null if not found.</returns>
    Task<Job?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the progress of a running job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="progress">The progress percentage (0-100).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpdateProgressAsync(string jobId, int progress, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cancels a job if it's pending or running.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task CancelAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes completed jobs older than the specified age.
    /// </summary>
    /// <param name="maxAge">The maximum age for completed jobs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of jobs removed.</returns>
    Task<int> CleanupCompletedJobsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
