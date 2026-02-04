namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents load test state.
/// </summary>
public enum LoadTestState
{
    /// <summary>
    /// Test is being initialized.
    /// </summary>
    Initializing,

    /// <summary>
    /// Test is running.
    /// </summary>
    Running,

    /// <summary>
    /// Test is completing.
    /// </summary>
    Completing,

    /// <summary>
    /// Test completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Test failed with errors.
    /// </summary>
    Failed,

    /// <summary>
    /// Test was cancelled.
    /// </summary>
    Cancelled
}