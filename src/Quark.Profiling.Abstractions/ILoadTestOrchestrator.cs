namespace Quark.Profiling.Abstractions;

/// <summary>
/// Provides load testing capabilities for Quark actors.
/// </summary>
public interface ILoadTestOrchestrator
{
    /// <summary>
    /// Starts a load test scenario.
    /// </summary>
    /// <param name="scenario">The load test scenario to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Load test result.</returns>
    Task<LoadTestResult> StartLoadTestAsync(LoadTestScenario scenario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a running load test.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns>Current load test status.</returns>
    LoadTestStatus? GetTestStatus(string testId);

    /// <summary>
    /// Stops a running load test.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopLoadTestAsync(string testId, CancellationToken cancellationToken = default);
}