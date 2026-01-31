using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;

namespace Quark.Placement.Locality;

/// <summary>
/// Placement policy that optimizes actor placement based on communication patterns.
/// Co-locates frequently communicating actors to minimize cross-silo network traffic.
/// </summary>
public sealed class LocalityAwarePlacementPolicy : IPlacementPolicy
{
    private readonly ICommunicationPatternAnalyzer _analyzer;
    private readonly IActorDirectory _actorDirectory;
    private readonly ILogger<LocalityAwarePlacementPolicy> _logger;
    private readonly LocalityAwarePlacementOptions _options;
    private readonly ConcurrentDictionary<string, string> _actorToSiloCache = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalityAwarePlacementPolicy"/> class.
    /// </summary>
    public LocalityAwarePlacementPolicy(
        ICommunicationPatternAnalyzer analyzer,
        IActorDirectory actorDirectory,
        IOptions<LocalityAwarePlacementOptions> options,
        ILogger<LocalityAwarePlacementPolicy> logger)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        if (availableSilos.Count == 0)
        {
            _logger.LogWarning("No available silos for actor {ActorType}:{ActorId}", actorType, actorId);
            return null;
        }

        // Periodic cleanup of old data
        PerformCleanupIfNeeded();

        // If only one silo available, use it
        if (availableSilos.Count == 1)
        {
            return availableSilos.First();
        }

        // Try to find the best silo based on communication patterns
        var bestSilo = SelectBestSiloBasedOnLocality(actorId, actorType, availableSilos);
        
        if (bestSilo != null)
        {
            _logger.LogDebug("Selected silo {SiloId} for actor {ActorType}:{ActorId} based on locality", 
                bestSilo, actorType, actorId);
            _actorToSiloCache[actorId] = bestSilo;
            return bestSilo;
        }

        // Fallback to load-balanced placement
        var randomSilo = SelectRandomSilo(availableSilos);
        _logger.LogDebug("Selected random silo {SiloId} for actor {ActorType}:{ActorId} (no locality data)", 
            randomSilo, actorType, actorId);
        
        if (randomSilo != null)
        {
            _actorToSiloCache[actorId] = randomSilo;
        }
        
        return randomSilo;
    }

    private string? SelectBestSiloBasedOnLocality(string actorId, string actorType, IReadOnlyCollection<string> availableSilos)
    {
        try
        {
            // Get communication graph
            var graph = _analyzer.GetCommunicationGraphAsync(_options.AnalysisWindow).GetAwaiter().GetResult();
            
            // Count how many actors this actor communicates with on each silo
            var siloScores = new Dictionary<string, double>();
            
            foreach (var silo in availableSilos)
            {
                siloScores[silo] = 0.0;
            }

            // Check outgoing communications (actors this actor sends messages to)
            if (graph.Edges.TryGetValue(actorId, out var targets))
            {
                foreach (var target in targets)
                {
                    var targetActorId = target.Key;
                    var metrics = target.Value;

                    // Only consider "hot" pairs
                    if (metrics.MessageCount >= _options.HotPairThreshold)
                    {
                        // Find which silo the target actor is on
                        if (_actorToSiloCache.TryGetValue(targetActorId, out var targetSilo))
                        {
                            if (availableSilos.Contains(targetSilo))
                            {
                                // Score is weighted by message count
                                siloScores[targetSilo] += metrics.MessageCount * _options.LocalityWeight;
                            }
                        }
                    }
                }
            }

            // Check incoming communications (actors that send messages to this actor)
            foreach (var edge in graph.Edges)
            {
                var sourceActorId = edge.Key;
                if (edge.Value.TryGetValue(actorId, out var metrics))
                {
                    if (metrics.MessageCount >= _options.HotPairThreshold)
                    {
                        if (_actorToSiloCache.TryGetValue(sourceActorId, out var sourceSilo))
                        {
                            if (availableSilos.Contains(sourceSilo))
                            {
                                siloScores[sourceSilo] += metrics.MessageCount * _options.LocalityWeight;
                            }
                        }
                    }
                }
            }

            // Select the silo with the highest score
            if (siloScores.Count > 0)
            {
                var bestScore = siloScores.Values.Max();
                if (bestScore > 0)
                {
                    var bestSilo = siloScores.First(kvp => kvp.Value == bestScore).Key;
                    return bestSilo;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting silo based on locality for actor {ActorType}:{ActorId}", actorType, actorId);
            return null;
        }
    }

    private string? SelectRandomSilo(IReadOnlyCollection<string> availableSilos)
    {
        // Use Random.Shared for thread-safe random selection (available in .NET 6+)
        var index = Random.Shared.Next(availableSilos.Count);
        return availableSilos.ElementAt(index);
    }

    private void PerformCleanupIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastCleanup >= _options.CleanupInterval)
        {
            try
            {
                _analyzer.ClearOldData(_options.MaxDataAge);
                _lastCleanup = now;
                _logger.LogDebug("Performed cleanup of old communication data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during communication data cleanup");
            }
        }
    }
}
