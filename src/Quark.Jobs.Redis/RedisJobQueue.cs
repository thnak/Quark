using System.Text.Json;
using Quark.Jobs;
using StackExchange.Redis;

namespace Quark.Jobs.Redis;

/// <summary>
///     Redis-based implementation of the job queue.
///     Uses Redis sorted sets and hashes for persistent job storage.
/// </summary>
public sealed class RedisJobQueue : IJobQueue
{
    private readonly IDatabase _database;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string JobKeyPrefix = "quark:job:";
    private const string PendingQueueKey = "quark:jobs:pending";
    private const string ScheduledQueueKey = "quark:jobs:scheduled";

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisJobQueue"/> class.
    /// </summary>
    /// <param name="database">The Redis database connection.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public RedisJobQueue(IDatabase database, JsonSerializerOptions? jsonOptions = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(job.JobId);

        // Check if job already exists
        var key = GetJobKey(job.JobId);
        if (await _database.KeyExistsAsync(key))
        {
            throw new InvalidOperationException($"Job with ID '{job.JobId}' already exists");
        }

        // Store job data
        var json = JsonSerializer.Serialize(job, _jsonOptions);
        await _database.StringSetAsync(key, json);

        // Add to appropriate queue
        if (job.ScheduledAt == null || job.ScheduledAt <= DateTimeOffset.UtcNow)
        {
            // Add to pending queue with priority as score (higher priority = higher score)
            var score = job.Priority;
            await _database.SortedSetAddAsync(PendingQueueKey, job.JobId, score);
        }
        else
        {
            // Add to scheduled queue with timestamp as score
            job.Status = JobStatus.Scheduled;
            await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));
            
            var score = job.ScheduledAt.Value.ToUnixTimeMilliseconds();
            await _database.SortedSetAddAsync(ScheduledQueueKey, job.JobId, score);
        }

        return job.JobId;
    }

    /// <inheritdoc />
    public async Task<Job?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        // First, move any ready scheduled jobs to pending queue
        await ProcessScheduledJobsAsync();

        // Try to dequeue from pending queue (highest priority first)
        var jobIds = await _database.SortedSetRangeByScoreAsync(
            PendingQueueKey,
            order: Order.Descending,
            take: 10);

        foreach (var jobIdValue in jobIds)
        {
            var jobId = jobIdValue.ToString();
            var job = await GetJobAsync(jobId, cancellationToken);

            if (job == null)
            {
                // Job was deleted, remove from queue
                await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);
                continue;
            }

            // Check if job is eligible for execution
            if (job.Status == JobStatus.Pending &&
                (job.ScheduledAt == null || job.ScheduledAt <= DateTimeOffset.UtcNow))
            {
                // Check dependencies
                if (await AreDependenciesMetAsync(job))
                {
                    // Mark as running
                    job.Status = JobStatus.Running;
                    job.StartedAt = DateTimeOffset.UtcNow;
                    job.UpdatedAt = DateTimeOffset.UtcNow;

                    // Update in Redis
                    var key = GetJobKey(jobId);
                    await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

                    // Remove from pending queue
                    await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);

                    return job;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string jobId, byte[]? result = null, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job == null)
            return;

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        job.Result = result;
        job.Progress = 100;

        var key = GetJobKey(jobId);
        await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

        // Remove from any queues
        await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);
        await _database.SortedSetRemoveAsync(ScheduledQueueKey, jobId);
    }

    /// <inheritdoc />
    public async Task FailAsync(string jobId, Exception exception, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job == null)
            return;

        job.AttemptCount++;
        job.LastError = exception.Message;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        if (job.AttemptCount < job.RetryPolicy.MaxRetries)
        {
            // Schedule retry
            var delay = job.RetryPolicy.GetDelay(job.AttemptCount);
            job.ScheduledAt = DateTimeOffset.UtcNow.Add(delay);
            job.Status = JobStatus.Pending;

            var key = GetJobKey(jobId);
            await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

            // Move to scheduled queue
            await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);
            var score = job.ScheduledAt.Value.ToUnixTimeMilliseconds();
            await _database.SortedSetAddAsync(ScheduledQueueKey, jobId, score);
        }
        else
        {
            // Max retries exceeded
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;

            var key = GetJobKey(jobId);
            await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

            // Remove from all queues
            await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);
            await _database.SortedSetRemoveAsync(ScheduledQueueKey, jobId);
        }
    }

    /// <inheritdoc />
    public async Task<Job?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var key = GetJobKey(jobId);
        var json = await _database.StringGetAsync(key);

        if (json.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<Job>(json.ToString(), _jsonOptions);
    }

    /// <inheritdoc />
    public async Task UpdateProgressAsync(string jobId, int progress, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job == null)
            return;

        job.Progress = Math.Clamp(progress, 0, 100);
        job.UpdatedAt = DateTimeOffset.UtcNow;

        var key = GetJobKey(jobId);
        await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));
    }

    /// <inheritdoc />
    public async Task CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job == null)
            return;

        if (job.Status == JobStatus.Pending || job.Status == JobStatus.Running)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            var key = GetJobKey(jobId);
            await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

            // Remove from all queues
            await _database.SortedSetRemoveAsync(PendingQueueKey, jobId);
            await _database.SortedSetRemoveAsync(ScheduledQueueKey, jobId);
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupCompletedJobsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTimeOffset.UtcNow - maxAge;
        var pattern = $"{JobKeyPrefix}*";
        var count = 0;

        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            var json = await _database.StringGetAsync(key);
            if (json.IsNullOrEmpty)
                continue;

            var job = JsonSerializer.Deserialize<Job>(json.ToString(), _jsonOptions);
            if (job?.Status == JobStatus.Completed && job.CompletedAt < cutoffTime)
            {
                await _database.KeyDeleteAsync(key);
                count++;
            }
        }

        return count;
    }

    private static string GetJobKey(string jobId) => $"{JobKeyPrefix}{jobId}";

    private async Task<bool> AreDependenciesMetAsync(Job job)
    {
        if (job.Dependencies == null || job.Dependencies.RequiredJobs.Count == 0)
            return true;

        var completedCount = 0;
        foreach (var depId in job.Dependencies.RequiredJobs)
        {
            var depJob = await GetJobAsync(depId);
            if (depJob?.Status == JobStatus.Completed)
            {
                completedCount++;
            }
        }

        if (job.Dependencies.RequireAll)
        {
            return completedCount == job.Dependencies.RequiredJobs.Count;
        }
        else
        {
            return completedCount > 0;
        }
    }

    private async Task ProcessScheduledJobsAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Get jobs that are ready to run
        var readyJobIds = await _database.SortedSetRangeByScoreAsync(
            ScheduledQueueKey,
            start: 0,
            stop: now);

        foreach (var jobIdValue in readyJobIds)
        {
            var jobId = jobIdValue.ToString();
            var job = await GetJobAsync(jobId);

            if (job != null && job.Status == JobStatus.Scheduled)
            {
                // Move to pending queue
                job.Status = JobStatus.Pending;
                var key = GetJobKey(jobId);
                await _database.StringSetAsync(key, JsonSerializer.Serialize(job, _jsonOptions));

                // Move from scheduled to pending queue
                await _database.SortedSetRemoveAsync(ScheduledQueueKey, jobId);
                await _database.SortedSetAddAsync(PendingQueueKey, jobId, job.Priority);
            }
        }
    }
}
