namespace Quark.Queries;

/// <summary>
/// Result of an actor query with pagination support.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed class ActorQueryResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorQueryResult{T}"/> class.
    /// </summary>
    public ActorQueryResult(
        IReadOnlyList<T> items,
        int totalCount,
        int pageNumber = 1,
        int pageSize = 100)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        HasNextPage = pageNumber < TotalPages;
        HasPreviousPage = pageNumber > 1;
    }

    /// <summary>
    /// Gets the items in the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the total number of items across all pages.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// Gets the current page number (1-based).
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages { get; }

    /// <summary>
    /// Gets whether there is a next page.
    /// </summary>
    public bool HasNextPage { get; }

    /// <summary>
    /// Gets whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage { get; }
}
