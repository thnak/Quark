namespace Quark.DurableTasks;

/// <summary>
///     Interface for activity functions that execute as part of an orchestration.
/// </summary>
/// <typeparam name="TInput">The input type for the activity.</typeparam>
/// <typeparam name="TOutput">The output type for the activity.</typeparam>
public interface IActivity<TInput, TOutput>
{
    /// <summary>
    ///     Gets the name of the activity (used for identification).
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Executes the activity with the given input.
    /// </summary>
    /// <param name="input">The activity input.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The activity output.</returns>
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}

/// <summary>
///     Base class for implementing activities.
/// </summary>
/// <typeparam name="TInput">The input type for the activity.</typeparam>
/// <typeparam name="TOutput">The output type for the activity.</typeparam>
public abstract class ActivityBase<TInput, TOutput> : IActivity<TInput, TOutput>
{
    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <inheritdoc />
    public abstract Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
