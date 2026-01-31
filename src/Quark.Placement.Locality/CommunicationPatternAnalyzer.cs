using System.Collections.Concurrent;

namespace Quark.Placement.Locality;

/// <summary>
/// Default implementation of <see cref="ICommunicationPatternAnalyzer"/>.
/// Tracks actor-to-actor message patterns for locality optimization.
/// </summary>
public sealed class CommunicationPatternAnalyzer : ICommunicationPatternAnalyzer
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationMetrics>> _interactions = new();
    private readonly object _cleanupLock = new();

    /// <inheritdoc />
    public void RecordInteraction(string fromActorId, string toActorId, long messageSize)
    {
        if (string.IsNullOrEmpty(fromActorId) || string.IsNullOrEmpty(toActorId))
            return;

        // Don't track self-messages
        if (fromActorId == toActorId)
            return;

        var targets = _interactions.GetOrAdd(fromActorId, _ => new ConcurrentDictionary<string, CommunicationMetrics>());
        
        targets.AddOrUpdate(
            toActorId,
            _ => new CommunicationMetrics
            {
                MessageCount = 1,
                TotalBytes = messageSize,
                LastInteraction = DateTimeOffset.UtcNow,
                AverageLatency = TimeSpan.Zero
            },
            (_, existing) =>
            {
                existing.MessageCount++;
                existing.TotalBytes += messageSize;
                existing.LastInteraction = DateTimeOffset.UtcNow;
                return existing;
            });
    }

    /// <inheritdoc />
    public Task<CommunicationGraph> GetCommunicationGraphAsync(TimeSpan window)
    {
        var graph = new CommunicationGraph();
        var cutoffTime = DateTimeOffset.UtcNow - window;

        foreach (var kvp in _interactions)
        {
            var fromActorId = kvp.Key;
            var targets = kvp.Value;

            foreach (var target in targets)
            {
                var toActorId = target.Key;
                var metrics = target.Value;

                // Only include recent interactions within the time window
                if (metrics.LastInteraction >= cutoffTime)
                {
                    graph.AddOrUpdateEdge(fromActorId, toActorId, 0);
                    var graphMetrics = graph.GetMetrics(fromActorId, toActorId);
                    if (graphMetrics != null)
                    {
                        graphMetrics.MessageCount = metrics.MessageCount;
                        graphMetrics.TotalBytes = metrics.TotalBytes;
                        graphMetrics.AverageLatency = metrics.AverageLatency;
                        graphMetrics.LastInteraction = metrics.LastInteraction;
                    }
                }
            }
        }

        return Task.FromResult(graph);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActorPair>> GetHotPairsAsync(int topN)
    {
        var pairs = new List<ActorPair>();

        foreach (var kvp in _interactions)
        {
            var fromActorId = kvp.Key;
            var targets = kvp.Value;

            foreach (var target in targets)
            {
                var toActorId = target.Key;
                var metrics = target.Value;
                pairs.Add(new ActorPair(fromActorId, toActorId, metrics));
            }
        }

        // Sort by message count (descending) and take top N
        var hotPairs = pairs
            .OrderByDescending(p => p.Metrics.MessageCount)
            .Take(topN)
            .ToList();

        return Task.FromResult<IReadOnlyList<ActorPair>>(hotPairs);
    }

    /// <inheritdoc />
    public void ClearOldData(TimeSpan maxAge)
    {
        lock (_cleanupLock)
        {
            var cutoffTime = DateTimeOffset.UtcNow - maxAge;
            var keysToRemove = new List<string>();

            foreach (var kvp in _interactions)
            {
                var fromActorId = kvp.Key;
                var targets = kvp.Value;
                var targetKeysToRemove = new List<string>();

                foreach (var target in targets)
                {
                    if (target.Value.LastInteraction < cutoffTime)
                    {
                        targetKeysToRemove.Add(target.Key);
                    }
                }

                foreach (var key in targetKeysToRemove)
                {
                    targets.TryRemove(key, out _);
                }

                if (targets.IsEmpty)
                {
                    keysToRemove.Add(fromActorId);
                }
            }

            foreach (var key in keysToRemove)
            {
                _interactions.TryRemove(key, out _);
            }
        }
    }
}
