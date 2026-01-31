using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Hosting;

namespace Quark.Queries;

/// <summary>
/// Default implementation of <see cref="IActorQueryService"/> that queries actors on a single silo.
/// </summary>
public sealed class ActorQueryService : IActorQueryService
{
    private readonly IQuarkSilo _silo;
    private readonly ILogger<ActorQueryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorQueryService"/> class.
    /// </summary>
    public ActorQueryService(IQuarkSilo silo, ILogger<ActorQueryService> logger)
    {
        _silo = silo ?? throw new ArgumentNullException(nameof(silo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<IActor>> GetAllActorsAsync(CancellationToken cancellationToken = default)
    {
        var actors = _silo.GetActiveActors();
        return Task.FromResult(actors);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<IActor>> QueryActorsAsync(
        Func<IActor, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var actors = _silo.GetActiveActors();
        var filtered = actors.Where(predicate).ToList();
        return Task.FromResult<IReadOnlyCollection<IActor>>(filtered);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<T>> QueryActorsByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IActor
    {
        var actors = _silo.GetActiveActors();
        var filtered = actors.OfType<T>().ToList();
        return Task.FromResult<IReadOnlyCollection<T>>(filtered);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<IActor>> QueryActorsByIdPatternAsync(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Pattern cannot be null or whitespace.", nameof(pattern));
        }

        var actors = _silo.GetActiveActors();
        var regex = ConvertGlobToRegex(pattern);
        var filtered = actors.Where(a => regex.IsMatch(a.ActorId)).ToList();
        return Task.FromResult<IReadOnlyCollection<IActor>>(filtered);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ActorMetadata>> GetActorMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        var actors = await GetAllActorsAsync(cancellationToken);
        var metadata = actors.Select(CreateActorMetadata).ToList();
        return metadata;
    }

    /// <inheritdoc />
    public async Task<ActorQueryResult<ActorMetadata>> QueryActorMetadataAsync(
        Func<ActorMetadata, bool> predicate,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (pageNumber < 1)
        {
            throw new ArgumentException("Page number must be at least 1.", nameof(pageNumber));
        }

        if (pageSize < 1 || pageSize > 1000)
        {
            throw new ArgumentException("Page size must be between 1 and 1000.", nameof(pageSize));
        }

        var allMetadata = await GetActorMetadataAsync(cancellationToken);
        var filtered = allMetadata.Where(predicate).ToList();
        var totalCount = filtered.Count;

        var paged = filtered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ActorQueryResult<ActorMetadata>(paged, totalCount, pageNumber, pageSize);
    }

    /// <inheritdoc />
    public Task<int> CountActorsAsync(CancellationToken cancellationToken = default)
    {
        var count = _silo.GetActiveActors().Count;
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public async Task<int> CountActorsAsync(
        Func<ActorMetadata, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var metadata = await GetActorMetadataAsync(cancellationToken);
        var count = metadata.Count(predicate);
        return count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> GroupActorsByTypeAsync(
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetActorMetadataAsync(cancellationToken);
        var grouped = metadata
            .GroupBy(m => m.ActorType)
            .ToDictionary(g => g.Key, g => g.Count());
        return grouped;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActorMetadata>> GetTopActorsAsync<TKey>(
        Func<ActorMetadata, TKey> selector,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        if (count < 1)
        {
            throw new ArgumentException("Count must be at least 1.", nameof(count));
        }

        var metadata = await GetActorMetadataAsync(cancellationToken);
        var top = metadata
            .OrderByDescending(selector)
            .Take(count)
            .ToList();
        return top;
    }

    private ActorMetadata CreateActorMetadata(IActor actor)
    {
        var type = actor.GetType();
        var attribute = type.GetCustomAttribute<ActorAttribute>();

        // Try to get activation time - this would need to be tracked separately in production
        // For now, we'll use a default value
        var activatedAt = DateTimeOffset.UtcNow; // TODO: Track actual activation time

        return new ActorMetadata(
            actorId: actor.ActorId,
            actorType: type.Name,
            fullTypeName: type.FullName ?? type.Name,
            isReentrant: attribute?.Reentrant ?? false,
            isStateless: attribute?.Stateless ?? false,
            activatedAt: activatedAt,
            customName: attribute?.Name
        );
    }

    private static Regex ConvertGlobToRegex(string pattern)
    {
        // Escape special regex characters except * and ?
        var escaped = Regex.Escape(pattern);
        // Convert glob wildcards to regex
        var regexPattern = escaped
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex($"^{regexPattern}$", RegexOptions.Compiled);
    }
}
