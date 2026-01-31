namespace Quark.Jobs;

/// <summary>
///     Fluent builder for creating job workflows with dependencies.
/// </summary>
public sealed class JobWorkflow
{
    private readonly List<JobWorkflowStep> _steps = new();
    private readonly Dictionary<string, string> _jobIds = new();

    /// <summary>
    ///     Adds a job to the workflow.
    /// </summary>
    /// <param name="stepName">Unique step name.</param>
    /// <param name="jobType">The job type identifier.</param>
    /// <param name="payload">The serialized job payload.</param>
    /// <param name="priority">Job priority (default: 0).</param>
    /// <param name="dependsOn">Names of steps this job depends on.</param>
    /// <returns>The workflow builder for chaining.</returns>
    public JobWorkflow AddJob(
        string stepName,
        string jobType,
        byte[] payload,
        int priority = 0,
        params string[] dependsOn)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepName);
        ArgumentException.ThrowIfNullOrEmpty(jobType);
        ArgumentNullException.ThrowIfNull(payload);

        if (_jobIds.ContainsKey(stepName))
        {
            throw new InvalidOperationException($"Step '{stepName}' already exists in workflow");
        }

        var jobId = $"{stepName}-{Guid.NewGuid():N}";
        _jobIds[stepName] = jobId;

        var step = new JobWorkflowStep
        {
            StepName = stepName,
            JobId = jobId,
            JobType = jobType,
            Payload = payload,
            Priority = priority,
            DependsOn = dependsOn.ToList()
        };

        _steps.Add(step);
        return this;
    }

    /// <summary>
    ///     Adds multiple parallel jobs to the workflow.
    /// </summary>
    /// <param name="stepNames">Unique names for each parallel step.</param>
    /// <param name="jobType">The job type identifier (same for all).</param>
    /// <param name="payloads">The payloads for each job.</param>
    /// <param name="dependsOn">Names of steps these jobs depend on.</param>
    /// <returns>The workflow builder for chaining.</returns>
    public JobWorkflow AddParallelJobs(
        string[] stepNames,
        string jobType,
        byte[][] payloads,
        params string[] dependsOn)
    {
        ArgumentNullException.ThrowIfNull(stepNames);
        ArgumentException.ThrowIfNullOrEmpty(jobType);
        ArgumentNullException.ThrowIfNull(payloads);

        if (stepNames.Length != payloads.Length)
        {
            throw new ArgumentException("Number of step names must match number of payloads");
        }

        for (int i = 0; i < stepNames.Length; i++)
        {
            AddJob(stepNames[i], jobType, payloads[i], priority: 0, dependsOn);
        }

        return this;
    }

    /// <summary>
    ///     Builds the workflow and returns all jobs with their dependencies resolved.
    /// </summary>
    /// <param name="retryPolicy">Optional retry policy to apply to all jobs.</param>
    /// <returns>List of jobs ready for enqueueing.</returns>
    public List<Job> Build(RetryPolicy? retryPolicy = null)
    {
        retryPolicy ??= RetryPolicy.Default;
        var jobs = new List<Job>();

        foreach (var step in _steps)
        {
            var job = new Job
            {
                JobId = step.JobId,
                JobType = step.JobType,
                Payload = step.Payload,
                Priority = step.Priority,
                RetryPolicy = retryPolicy,
                Status = JobStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Resolve dependencies
            if (step.DependsOn.Count > 0)
            {
                var dependencyJobIds = step.DependsOn
                    .Select(stepName =>
                    {
                        if (!_jobIds.TryGetValue(stepName, out var depJobId))
                        {
                            throw new InvalidOperationException(
                                $"Step '{step.StepName}' depends on unknown step '{stepName}'");
                        }
                        return depJobId;
                    })
                    .ToList();

                job.Dependencies = new JobDependencies
                {
                    RequiredJobs = dependencyJobIds,
                    RequireAll = true
                };
            }

            jobs.Add(job);
        }

        return jobs;
    }

    /// <summary>
    ///     Gets the number of steps in the workflow.
    /// </summary>
    public int StepCount => _steps.Count;
}

internal sealed class JobWorkflowStep
{
    public required string StepName { get; init; }
    public required string JobId { get; init; }
    public required string JobType { get; init; }
    public required byte[] Payload { get; init; }
    public int Priority { get; init; }
    public List<string> DependsOn { get; init; } = new();
}
