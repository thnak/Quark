namespace Quark.Abstractions.Persistence;

/// <summary>
///     Interface for state storage providers (SQL, Redis, etc.).
///     Supports optimistic concurrency control via version numbers (E-Tags).
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
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads the state with version information for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The loaded state with version, or null if not found.</returns>
    Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves the state for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves the state for the specified actor with optimistic concurrency check.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="state">The state to save.</param>
    /// <param name="expectedVersion">The expected version. Use null for first save, or the version from LoadWithVersionAsync.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new version number after save.</returns>
    /// <exception cref="ConcurrencyException">Thrown when the version doesn't match (someone else modified the state).</exception>
    Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the state for the specified actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="stateName">The name of the state property.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default);
}