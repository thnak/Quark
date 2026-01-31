using Quark.Abstractions;

namespace Quark.Queries;

/// <summary>
/// Service for querying active actors with LINQ-style syntax.
/// </summary>
public interface IActorQueryService
{
    /// <summary>
    /// Gets all active actors.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of active actors.</returns>
    Task<IReadOnlyCollection<IActor>> GetAllActorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets actors matching the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to filter actors.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of matching actors.</returns>
    Task<IReadOnlyCollection<IActor>> QueryActorsAsync(
        Func<IActor, bool> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets actors of a specific type.
    /// </summary>
    /// <typeparam name="T">The actor type.</typeparam>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of actors of the specified type.</returns>
    Task<IReadOnlyCollection<T>> QueryActorsByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IActor;

    /// <summary>
    /// Gets actors whose IDs match the specified pattern.
    /// </summary>
    /// <param name="pattern">A glob-style pattern (* for wildcard).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of matching actors.</returns>
    Task<IReadOnlyCollection<IActor>> QueryActorsByIdPatternAsync(
        string pattern,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for all active actors.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of actor metadata.</returns>
    Task<IReadOnlyCollection<ActorMetadata>> GetActorMetadataAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for actors matching the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to filter actor metadata.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A paginated result of actor metadata.</returns>
    Task<ActorQueryResult<ActorMetadata>> QueryActorMetadataAsync(
        Func<ActorMetadata, bool> predicate,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of active actors.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of active actors.</returns>
    Task<int> CountActorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of actors matching the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to filter actors.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of matching actors.</returns>
    Task<int> CountActorsAsync(
        Func<ActorMetadata, bool> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets actors grouped by type.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A dictionary mapping actor types to counts.</returns>
    Task<IReadOnlyDictionary<string, int>> GroupActorsByTypeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top N actors based on a selector.
    /// </summary>
    /// <typeparam name="TKey">The type of the key to order by.</typeparam>
    /// <param name="selector">A function to extract the key to order by.</param>
    /// <param name="count">The number of actors to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The top N actors.</returns>
    Task<IReadOnlyList<ActorMetadata>> GetTopActorsAsync<TKey>(
        Func<ActorMetadata, TKey> selector,
        int count,
        CancellationToken cancellationToken = default);
}
