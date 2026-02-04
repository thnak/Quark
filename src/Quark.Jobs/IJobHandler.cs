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