using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;
using System.Collections.Concurrent;

namespace Quark.Client;

/// <summary>
/// Smart router with local bypass optimization and location caching.
/// </summary>
public sealed class SmartRouter : ISmartRouter, IDisposable
{
    private readonly IActorDirectory _actorDirectory;
    private readonly IQuarkClusterMembership _clusterMembership;
    private readonly SmartRoutingOptions _options;
    private readonly ILogger<SmartRouter> _logger;
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, long> _statistics;
    private readonly string? _localSiloId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartRouter"/> class.
    /// </summary>
    public SmartRouter(
        IActorDirectory actorDirectory,
        IQuarkClusterMembership clusterMembership,
        IOptions<SmartRoutingOptions> options,
        ILogger<SmartRouter> logger,
        string? localSiloId = null)
    {
        _actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localSiloId = localSiloId;

        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.CacheSize
        });

        _statistics = new ConcurrentDictionary<string, long>();
        InitializeStatistics();
    }

    private void InitializeStatistics()
    {
        if (_options.EnableStatistics)
        {
            _statistics["TotalRequests"] = 0;
            _statistics["LocalSiloHits"] = 0;
            _statistics["SameProcessHits"] = 0;
            _statistics["RemoteHits"] = 0;
            _statistics["CacheHits"] = 0;
            _statistics["CacheMisses"] = 0;
        }
    }

    /// <inheritdoc />
    public async Task<RoutingDecision> RouteAsync(
        string actorId,
        string actorType,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            // Fallback to basic routing
            var siloId = _clusterMembership.GetActorSilo(actorId, actorType);
            return new RoutingDecision(actorId, actorType, RoutingResult.Remote, siloId);
        }

        IncrementStatistic("TotalRequests");

        // Check cache first
        var cacheKey = $"{actorType}:{actorId}";
        if (_cache.TryGetValue<RoutingDecision>(cacheKey, out var cachedDecision))
        {
            IncrementStatistic("CacheHits");
            _logger.LogDebug("Cache hit for actor {ActorId} ({ActorType})", actorId, actorType);
            return cachedDecision;
        }

        IncrementStatistic("CacheMisses");

        // Look up actor location in directory
        var location = await _actorDirectory.LookupActorAsync(actorId, actorType, cancellationToken);
        
        RoutingDecision decision;

        if (location == null)
        {
            // Actor not yet activated, use placement policy
            var targetSiloId = _clusterMembership.GetActorSilo(actorId, actorType);
            
            if (targetSiloId == null)
            {
                decision = new RoutingDecision(actorId, actorType, RoutingResult.NotFound);
            }
            else if (_options.EnableLocalBypass && targetSiloId == _localSiloId)
            {
                decision = new RoutingDecision(actorId, actorType, RoutingResult.LocalSilo, targetSiloId);
                IncrementStatistic("LocalSiloHits");
                _logger.LogDebug("Local silo routing for actor {ActorId} ({ActorType})", actorId, actorType);
            }
            else
            {
                decision = new RoutingDecision(actorId, actorType, RoutingResult.Remote, targetSiloId);
                IncrementStatistic("RemoteHits");
            }
        }
        else
        {
            // Actor is already activated
            if (_options.EnableSameProcessOptimization && location.SiloId == _localSiloId)
            {
                // Same process optimization
                decision = new RoutingDecision(actorId, actorType, RoutingResult.SameProcess, location.SiloId);
                IncrementStatistic("SameProcessHits");
                _logger.LogDebug("Same-process routing for actor {ActorId} ({ActorType})", actorId, actorType);
            }
            else if (_options.EnableLocalBypass && location.SiloId == _localSiloId)
            {
                // Local silo bypass
                decision = new RoutingDecision(actorId, actorType, RoutingResult.LocalSilo, location.SiloId);
                IncrementStatistic("LocalSiloHits");
                _logger.LogDebug("Local silo routing for actor {ActorId} ({ActorType})", actorId, actorType);
            }
            else
            {
                // Remote call required
                decision = new RoutingDecision(actorId, actorType, RoutingResult.Remote, location.SiloId);
                IncrementStatistic("RemoteHits");
            }
        }

        // Cache the decision
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.CacheTtl,
            Size = 1
        };
        _cache.Set(cacheKey, decision, cacheOptions);

        return decision;
    }

    /// <inheritdoc />
    public void InvalidateCache(string actorId, string actorType)
    {
        var cacheKey = $"{actorType}:{actorId}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for actor {ActorId} ({ActorType})", actorId, actorType);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, long> GetRoutingStatistics()
    {
        if (!_options.EnableStatistics)
        {
            return new Dictionary<string, long>();
        }

        return new Dictionary<string, long>(_statistics);
    }

    private void IncrementStatistic(string key)
    {
        if (_options.EnableStatistics)
        {
            _statistics.AddOrUpdate(key, 1, (_, count) => count + 1);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Dispose();
    }
}
