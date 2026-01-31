namespace Quark.DurableTasks;

/// <summary>
///     Interface for storing and retrieving orchestration state.
/// </summary>
public interface IOrchestrationStateStore
{
    /// <summary>
    ///     Saves the orchestration state.
    /// </summary>
    Task SaveStateAsync(OrchestrationState state, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads the orchestration state.
    /// </summary>
    Task<OrchestrationState?> LoadStateAsync(string orchestrationId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the orchestration state.
    /// </summary>
    Task DeleteStateAsync(string orchestrationId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Interface for invoking activities from orchestrations.
/// </summary>
public interface IActivityInvoker
{
    /// <summary>
    ///     Invokes an activity by name with the given input.
    /// </summary>
    Task<byte[]> InvokeAsync(string activityName, byte[] input, CancellationToken cancellationToken = default);
}
