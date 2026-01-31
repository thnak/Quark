namespace Quark.Sagas;

/// <summary>
/// Represents the execution status of a saga.
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// The saga has not yet started.
    /// </summary>
    NotStarted = 0,

    /// <summary>
    /// The saga is currently executing forward steps.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The saga has completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The saga encountered a failure and is compensating (rolling back).
    /// </summary>
    Compensating = 3,

    /// <summary>
    /// The saga has been compensated (rolled back) successfully.
    /// </summary>
    Compensated = 4,

    /// <summary>
    /// The saga failed during compensation.
    /// </summary>
    CompensationFailed = 5
}
