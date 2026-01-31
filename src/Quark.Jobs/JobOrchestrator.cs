using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Quark.Jobs;

/// <summary>
///     Orchestrates job execution by managing workers and dispatching jobs.
/// </summary>
public sealed class JobOrchestrator
{
    private readonly IJobQueue _jobQueue;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly Dictionary<string, Func<byte[], JobContext, CancellationToken, Task<object?>>> _handlers = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _workerCount;
    private readonly List<Task> _workers = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="JobOrchestrator"/> class.
    /// </summary>
    /// <param name="jobQueue">The job queue.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="workerCount">Number of concurrent workers (default: 4).</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public JobOrchestrator(
        IJobQueue jobQueue,
        ILogger<JobOrchestrator> logger,
        int workerCount = 4,
        JsonSerializerOptions? jsonOptions = null)
    {
        _jobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workerCount = workerCount;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    ///     Registers a job handler for a specific job type.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="jobType">The job type identifier.</param>
    /// <param name="handler">The handler function.</param>
    public void RegisterHandler<TPayload>(
        string jobType,
        IJobHandler<TPayload> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobType);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[jobType] = async (payload, context, ct) =>
        {
            var typedPayload = JsonSerializer.Deserialize<TPayload>(payload, _jsonOptions);
            if (typedPayload == null)
            {
                throw new InvalidOperationException($"Failed to deserialize payload for job type '{jobType}'");
            }

            return await handler.ExecuteAsync(typedPayload, context, ct);
        };
    }

    /// <summary>
    ///     Starts the job orchestrator workers.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            throw new InvalidOperationException("Job orchestrator is already running");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workers.Clear();

        _logger.LogInformation("Starting {WorkerCount} job workers", _workerCount);

        for (int i = 0; i < _workerCount; i++)
        {
            var workerId = i + 1;
            _workers.Add(Task.Run(() => WorkerLoopAsync(workerId, _cts.Token), _cts.Token));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Stops the job orchestrator workers.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _logger.LogInformation("Stopping job orchestrator");

        _cts.Cancel();

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _workers.Clear();
        }

        _logger.LogInformation("Job orchestrator stopped");
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} started", workerId);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobQueue.DequeueAsync(cancellationToken);

                if (job == null)
                {
                    // No jobs available, wait before trying again
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                _logger.LogInformation("Worker {WorkerId} processing job {JobId} of type {JobType}",
                    workerId, job.JobId, job.JobType);

                await ProcessJobAsync(job, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} encountered an error", workerId);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        _logger.LogInformation("Worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessJobAsync(Job job, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            _logger.LogError("No handler registered for job type '{JobType}'", job.JobType);
            await _jobQueue.FailAsync(
                job.JobId,
                new InvalidOperationException($"No handler registered for job type '{job.JobType}'"),
                cancellationToken);
            return;
        }

        try
        {
            var context = new JobContext
            {
                JobId = job.JobId,
                JobType = job.JobType,
                AttemptNumber = job.AttemptCount + 1,
                Metadata = job.Metadata?.AsReadOnly(),
                UpdateProgress = progress => _jobQueue.UpdateProgressAsync(job.JobId, progress, cancellationToken)
            };

            // Apply timeout if specified
            using var cts = job.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;

            if (cts != null && job.Timeout.HasValue)
            {
                cts.CancelAfter(job.Timeout.Value);
            }
            var effectiveCt = cts?.Token ?? cancellationToken;

            var result = await handler(job.Payload, context, effectiveCt);

            // Serialize result if present
            byte[]? serializedResult = null;
            if (result != null)
            {
                serializedResult = JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }

            await _jobQueue.CompleteAsync(job.JobId, serializedResult, cancellationToken);

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed with error: {Error}", job.JobId, ex.Message);
            await _jobQueue.FailAsync(job.JobId, ex, cancellationToken);
        }
    }
}
