using System.Collections.Concurrent;

namespace Quark.Jobs;

/// <summary>
///     In-memory implementation of the job queue for development and testing.
///     NOT suitable for production use as jobs are lost on restart.
/// </summary>
public sealed class InMemoryJobQueue : IJobQueue
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentQueue<string> _pendingQueue = new();

    /// <inheritdoc />
    public Task<string> EnqueueAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(job.JobId);

        if (!_jobs.TryAdd(job.JobId, job))
        {
            throw new InvalidOperationException($"Job with ID '{job.JobId}' already exists");
        }

        if (job.ScheduledAt == null || job.ScheduledAt <= DateTimeOffset.UtcNow)
        {
            _pendingQueue.Enqueue(job.JobId);
        }
        else
        {
            job.Status = JobStatus.Scheduled;
        }

        return Task.FromResult(job.JobId);
    }

    /// <inheritdoc />
    public Task<Job?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        // Try to dequeue a pending job
        while (_pendingQueue.TryDequeue(out var jobId))
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                // Check if job is still eligible for execution
                if (job.Status == JobStatus.Pending &&
                    (job.ScheduledAt == null || job.ScheduledAt <= DateTimeOffset.UtcNow))
                {
                    // Check dependencies
                    if (AreDependenciesMet(job))
                    {
                        job.Status = JobStatus.Running;
                        job.StartedAt = DateTimeOffset.UtcNow;
                        job.UpdatedAt = DateTimeOffset.UtcNow;
                        return Task.FromResult<Job?>(job);
                    }
                    else
                    {
                        // Re-enqueue to check later
                        _pendingQueue.Enqueue(jobId);
                    }
                }
            }
        }

        // Check for scheduled jobs that are now ready
        ProcessScheduledJobs();

        return Task.FromResult<Job?>(null);
    }

    /// <inheritdoc />
    public Task CompleteAsync(string jobId, byte[]? result = null, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.Result = result;
            job.Progress = 100;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(string jobId, Exception exception, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.AttemptCount++;
            job.LastError = exception.Message;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            if (job.AttemptCount < job.RetryPolicy.MaxRetries)
            {
                // Schedule retry
                var delay = job.RetryPolicy.GetDelay(job.AttemptCount);
                job.ScheduledAt = DateTimeOffset.UtcNow.Add(delay);
                job.Status = JobStatus.Pending;
                _pendingQueue.Enqueue(jobId);
            }
            else
            {
                // Max retries exceeded
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Job?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc />
    public Task UpdateProgressAsync(string jobId, int progress, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Progress = Math.Clamp(progress, 0, 100);
            job.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == JobStatus.Pending || job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CleanupCompletedJobsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - maxAge;
        var toRemove = _jobs.Values
            .Where(j => j.Status == JobStatus.Completed && j.CompletedAt < cutoffTime)
            .Select(j => j.JobId)
            .ToList();

        foreach (var jobId in toRemove)
        {
            _jobs.TryRemove(jobId, out _);
        }

        return Task.FromResult(toRemove.Count);
    }

    /// <summary>
    ///     Gets the total number of jobs (for testing).
    /// </summary>
    public int JobCount => _jobs.Count;

    /// <summary>
    ///     Clears all jobs (for testing).
    /// </summary>
    public void Clear()
    {
        _jobs.Clear();
        while (_pendingQueue.TryDequeue(out _)) { }
    }

    private bool AreDependenciesMet(Job job)
    {
        if (job.Dependencies == null || job.Dependencies.RequiredJobs.Count == 0)
            return true;

        var completedDependencies = job.Dependencies.RequiredJobs
            .Count(depId => _jobs.TryGetValue(depId, out var depJob) && depJob.Status == JobStatus.Completed);

        if (job.Dependencies.RequireAll)
        {
            return completedDependencies == job.Dependencies.RequiredJobs.Count;
        }
        else
        {
            return completedDependencies > 0;
        }
    }

    private void ProcessScheduledJobs()
    {
        var now = DateTimeOffset.UtcNow;
        var readyJobs = _jobs.Values
            .Where(j => j.Status == JobStatus.Scheduled && j.ScheduledAt <= now)
            .ToList();

        foreach (var job in readyJobs)
        {
            job.Status = JobStatus.Pending;
            _pendingQueue.Enqueue(job.JobId);
        }
    }
}
