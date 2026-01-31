namespace Quark.Jobs;

/// <summary>
///     Defines dependencies between jobs in a workflow.
/// </summary>
public sealed class JobDependencies
{
    /// <summary>
    ///     Gets or sets the job IDs that must complete successfully before this job can run.
    /// </summary>
    public List<string> RequiredJobs { get; set; } = new();

    /// <summary>
    ///     Gets or sets whether ALL dependencies must complete (true) or just ONE (false).
    /// </summary>
    public bool RequireAll { get; set; } = true;

    /// <summary>
    ///     Gets or sets the timeout for waiting on dependencies.
    /// </summary>
    public TimeSpan? DependencyTimeout { get; set; }

    /// <summary>
    ///     Creates dependencies requiring all specified jobs to complete.
    /// </summary>
    public static JobDependencies All(params string[] jobIds) => new()
    {
        RequiredJobs = jobIds.ToList(),
        RequireAll = true
    };

    /// <summary>
    ///     Creates dependencies requiring any one of the specified jobs to complete.
    /// </summary>
    public static JobDependencies Any(params string[] jobIds) => new()
    {
        RequiredJobs = jobIds.ToList(),
        RequireAll = false
    };
}
