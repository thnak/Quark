namespace Quark.Sagas;

/// <summary>
/// Represents a single step in a saga workflow.
/// </summary>
/// <typeparam name="TContext">The type of context passed between saga steps.</typeparam>
public interface ISagaStep<TContext>
{
    /// <summary>
    /// Gets the unique name of this saga step.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the forward action of this saga step.
    /// </summary>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compensates (rolls back) the effect of this saga step.
    /// This method should be idempotent to handle retries.
    /// </summary>
    /// <param name="context">The saga context containing shared data.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CompensateAsync(TContext context, CancellationToken cancellationToken = default);
}
