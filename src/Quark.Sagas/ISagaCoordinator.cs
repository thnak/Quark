namespace Quark.Sagas;

/// <summary>
/// Coordinates saga execution with state persistence and recovery.
/// </summary>
/// <typeparam name="TContext">The type of context passed between saga steps.</typeparam>
public interface ISagaCoordinator<TContext>
{
    /// <summary>
    /// Starts a new saga execution.
    /// </summary>
    /// <param name="saga">The saga to execute.</param>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The final status of the saga execution.</returns>
    Task<SagaStatus> StartSagaAsync(
        ISaga<TContext> saga,
        TContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a saga from its last checkpoint.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga to resume.</param>
    /// <param name="saga">The saga definition.</param>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The final status of the saga execution.</returns>
    Task<SagaStatus> ResumeSagaAsync(
        string sagaId,
        ISaga<TContext> saga,
        TContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a saga.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The saga state, or null if not found.</returns>
    Task<SagaState?> GetSagaStateAsync(string sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers all sagas in running or compensating state.
    /// This should be called on startup to resume interrupted sagas.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of sagas recovered.</returns>
    Task<int> RecoverInProgressSagasAsync(CancellationToken cancellationToken = default);
}
