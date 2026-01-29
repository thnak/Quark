namespace Quark.Abstractions;

/// <summary>
///     Base interface for all actors in the Quark framework.
///     Actors are lightweight, stateful objects that process messages sequentially.
/// </summary>
public interface IActor
{
    /// <summary>
    ///     Gets the unique identifier for this actor instance.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    ///     Called when the actor is activated.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task OnActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Called when the actor is deactivated.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task OnDeactivateAsync(CancellationToken cancellationToken = default);
}