namespace Quark.Abstractions.Persistence;

/// <summary>
///     Interface for state storage providers (SQL, Redis, etc.).
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public interface IStateStorage<TState> where TState : class
{
    /// <summary>
    ///     Loads the state for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The loaded state, or null if not found.</returns>
    Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves the state for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the state for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default);
}