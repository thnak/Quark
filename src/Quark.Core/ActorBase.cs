namespace Quark.Core;

/// <summary>
/// Base class for actor implementations.
/// Provides common functionality for all actors.
/// </summary>
public abstract class ActorBase : IActor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorBase"/> class.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    protected ActorBase(string actorId)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
    }

    /// <inheritdoc />
    public string ActorId { get; }

    /// <summary>
    /// Called when the actor is activated.
    /// Override this method to perform initialization logic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor is deactivated.
    /// Override this method to perform cleanup logic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
