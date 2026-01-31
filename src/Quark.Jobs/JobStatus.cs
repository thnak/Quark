namespace Quark.Jobs;

/// <summary>
///     Represents the status of a job.
/// </summary>
public enum JobStatus
{
    /// <summary>
    ///     Job is waiting to be executed.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     Job is currently being executed.
    /// </summary>
    Running = 1,

    /// <summary>
    ///     Job completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    ///     Job execution failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     Job was cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    ///     Job is scheduled for future execution.
    /// </summary>
    Scheduled = 5
}
