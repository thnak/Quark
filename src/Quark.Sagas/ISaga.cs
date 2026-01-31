namespace Quark.Sagas;

/// <summary>
/// Represents a saga that orchestrates a multi-step distributed transaction.
/// </summary>
/// <typeparam name="TContext">The type of context passed between saga steps.</typeparam>
public interface ISaga<TContext>
{
    /// <summary>
    /// Gets the unique identifier of this saga instance.
    /// </summary>
    string SagaId { get; }

    /// <summary>
    /// Gets the ordered list of steps that make up this saga.
    /// </summary>
    IReadOnlyList<ISagaStep<TContext>> Steps { get; }

    /// <summary>
    /// Gets the current state of the saga execution.
    /// </summary>
    SagaState State { get; }

    /// <summary>
    /// Executes the saga from the beginning or resumes from a checkpoint.
    /// </summary>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<SagaStatus> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compensates all completed steps in reverse order.
    /// </summary>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CompensateAsync(TContext context, CancellationToken cancellationToken = default);
}
