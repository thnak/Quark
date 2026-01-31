namespace Quark.Sagas;

/// <summary>
/// Interface for persisting saga state across restarts and failures.
/// </summary>
public interface ISagaStateStore
{
    /// <summary>
    /// Saves the current state of a saga.
    /// </summary>
    /// <param name="state">The saga state to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveStateAsync(SagaState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the state of a saga by its identifier.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The saga state, or null if not found.</returns>
    Task<SagaState?> LoadStateAsync(string sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the state of a completed or compensated saga.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteStateAsync(string sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all saga states matching a specific status.
    /// Useful for finding sagas that need recovery.
    /// </summary>
    /// <param name="status">The saga status to filter by.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of saga states matching the status.</returns>
    Task<IReadOnlyList<SagaState>> GetSagasByStatusAsync(
        SagaStatus status,
        CancellationToken cancellationToken = default);
}
