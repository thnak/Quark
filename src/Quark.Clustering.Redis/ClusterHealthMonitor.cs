using System.Collections.Concurrent;
using System.Text.Json;
using Quark.Abstractions.Clustering;
using StackExchange.Redis;

namespace Quark.Clustering.Redis;

/// <summary>
///     Monitors cluster health and coordinates automatic silo eviction.
/// </summary>
public sealed class ClusterHealthMonitor : IClusterHealthMonitor, IDisposable
{
    private const string HealthScoreKeyPrefix = "quark:health:";
    private const string HealthHistoryKeyPrefix = "quark:health:history:";
    private const int MaxHistorySize = 20;

    private readonly IConnectionMultiplexer _redis;
    private readonly IClusterMembership _clusterMembership;
    private readonly IHealthScoreCalculator _healthScoreCalculator;
    private readonly ConcurrentDictionary<string, int> _unhealthyCheckCounts;
    private readonly Timer? _healthCheckTimer;
    private bool _isRunning;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ClusterHealthMonitor" /> class.
    /// </summary>
    public ClusterHealthMonitor(
        IConnectionMultiplexer redis,
        IClusterMembership clusterMembership,
        IHealthScoreCalculator healthScoreCalculator,
        EvictionPolicyOptions? options = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _healthScoreCalculator = healthScoreCalculator ?? throw new ArgumentNullException(nameof(healthScoreCalculator));
        Options = options ?? new EvictionPolicyOptions();
        _unhealthyCheckCounts = new ConcurrentDictionary<string, int>();
        _healthCheckTimer = new Timer(HealthCheckCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public EvictionPolicyOptions Options { get; }

    /// <inheritdoc />
    public event EventHandler<SiloEvictedEventArgs>? SiloEvicted;

    /// <inheritdoc />
    public event EventHandler<SiloHealthDegradedEventArgs>? SiloHealthDegraded;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _isRunning = true;
        _healthCheckTimer?.Change(
            TimeSpan.FromSeconds(Options.HealthCheckIntervalSeconds),
            TimeSpan.FromSeconds(Options.HealthCheckIntervalSeconds));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UpdateHealthScoreAsync(SiloHealthScore healthScore, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var siloId = _clusterMembership.CurrentSiloId;
        
        // Store current health score
        var healthKey = HealthScoreKeyPrefix + siloId;
        var healthData = JsonSerializer.Serialize(healthScore, QuarkJsonSerializerContext.Default.SiloHealthScore);
        await db.StringSetAsync(healthKey, healthData, TimeSpan.FromSeconds(Options.HeartbeatTimeoutSeconds * 2));

        // Add to history (using a list, keep last N entries)
        var historyKey = HealthHistoryKeyPrefix + siloId;
        await db.ListLeftPushAsync(historyKey, healthData);
        await db.ListTrimAsync(historyKey, 0, MaxHistorySize - 1);
        await db.KeyExpireAsync(historyKey, TimeSpan.FromSeconds(Options.HeartbeatTimeoutSeconds * 2));
    }

    /// <inheritdoc />
    public async Task<SiloHealthScore?> GetHealthScoreAsync(string siloId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var healthKey = HealthScoreKeyPrefix + siloId;
        var data = await db.StringGetAsync(healthKey);

        if (data.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize(data.ToString(), QuarkJsonSerializerContext.Default.SiloHealthScore);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SiloHealthScore>> GetHealthScoreHistoryAsync(
        string siloId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var historyKey = HealthHistoryKeyPrefix + siloId;
        var historyData = await db.ListRangeAsync(historyKey, 0, count - 1);

        var scores = new List<SiloHealthScore>();
        foreach (var data in historyData)
        {
            if (!data.IsNullOrEmpty)
            {
                var score = JsonSerializer.Deserialize(data.ToString(), QuarkJsonSerializerContext.Default.SiloHealthScore);
                if (score != null)
                    scores.Add(score);
            }
        }

        // Reverse to get chronological order (oldest first)
        scores.Reverse();
        return scores.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task PerformHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (Options.Policy == SiloEvictionPolicy.None)
            return;

        var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var silo in silos)
        {
            // Skip our own silo
            if (silo.SiloId == _clusterMembership.CurrentSiloId)
                continue;

            var shouldEvict = false;
            var evictionReason = string.Empty;

            // Check timeout-based eviction
            if (Options.Policy == SiloEvictionPolicy.TimeoutBased || Options.Policy == SiloEvictionPolicy.Hybrid)
            {
                var timeSinceHeartbeat = now - silo.LastHeartbeat;
                if (timeSinceHeartbeat.TotalSeconds > Options.HeartbeatTimeoutSeconds)
                {
                    shouldEvict = true;
                    evictionReason = $"Heartbeat timeout ({timeSinceHeartbeat.TotalSeconds:F1}s)";
                }
            }

            // Check health-score-based eviction
            if (!shouldEvict && (Options.Policy == SiloEvictionPolicy.HealthScoreBased || Options.Policy == SiloEvictionPolicy.Hybrid))
            {
                var healthScore = await GetHealthScoreAsync(silo.SiloId, cancellationToken);
                if (healthScore != null)
                {
                    if (!healthScore.IsHealthy(Options.HealthScoreThreshold))
                    {
                        var unhealthyCount = _unhealthyCheckCounts.AddOrUpdate(silo.SiloId, 1, (_, count) => count + 1);
                        
                        if (unhealthyCount >= Options.ConsecutiveUnhealthyChecks)
                        {
                            shouldEvict = true;
                            evictionReason = $"Health score below threshold ({healthScore.OverallScore:F1} < {Options.HealthScoreThreshold})";
                        }
                        else
                        {
                            // Raise degradation event
                            var history = await GetHealthScoreHistoryAsync(silo.SiloId, 10, cancellationToken);
                            var predictedFailure = _healthScoreCalculator.PredictFailure(history);
                            
                            SiloHealthDegraded?.Invoke(this, new SiloHealthDegradedEventArgs(
                                silo, healthScore, predictedFailure));
                        }
                    }
                    else
                    {
                        // Reset unhealthy count if health improved
                        _unhealthyCheckCounts.TryRemove(silo.SiloId, out _);
                    }
                }
            }

            // Perform eviction if needed
            if (shouldEvict)
            {
                await EvictSiloAsync(silo, evictionReason, cancellationToken);
            }
        }

        // Check for split-brain if enabled
        if (Options.EnableSplitBrainDetection && silos.Count >= Options.MinimumClusterSizeForQuorum)
        {
            await DetectSplitBrainAsync(silos, cancellationToken);
        }
    }

    /// <summary>
    ///     Evicts a silo from the cluster.
    /// </summary>
    private async Task EvictSiloAsync(SiloInfo silo, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            
            // Remove silo from cluster membership
            var siloKey = "quark:silo:" + silo.SiloId;
            await db.KeyDeleteAsync(siloKey);

            // Remove health data
            var healthKey = HealthScoreKeyPrefix + silo.SiloId;
            var historyKey = HealthHistoryKeyPrefix + silo.SiloId;
            await db.KeyDeleteAsync(healthKey);
            await db.KeyDeleteAsync(historyKey);

            // Clear unhealthy count
            _unhealthyCheckCounts.TryRemove(silo.SiloId, out _);

            // Raise eviction event
            SiloEvicted?.Invoke(this, new SiloEvictedEventArgs(silo, reason));

            // Trigger rebalancing if enabled
            if (Options.EnableAutomaticRebalancing)
            {
                // Note: Rebalancing is handled by the cluster membership's SiloLeft event
                // which will trigger actor redistribution in the hash ring
            }
        }
        catch
        {
            // Log error but don't throw - health checks should continue
        }
    }

    /// <summary>
    ///     Detects potential split-brain scenarios.
    /// </summary>
    private async Task DetectSplitBrainAsync(IReadOnlyCollection<SiloInfo> silos, CancellationToken cancellationToken)
    {
        // Split-brain detection: Check if we have network partitions
        // by verifying that silos can communicate with each other
        
        // Simple approach: If more than half the cluster has very high latency,
        // we might be in a partition
        var highLatencySilos = 0;
        
        foreach (var silo in silos)
        {
            if (silo.SiloId == _clusterMembership.CurrentSiloId)
                continue;

            var healthScore = await GetHealthScoreAsync(silo.SiloId, cancellationToken);
            if (healthScore != null && healthScore.NetworkLatencyMs > 1000) // 1 second
            {
                highLatencySilos++;
            }
        }

        // If more than half have high latency, we might be partitioned
        var majorityThreshold = silos.Count / 2;
        if (highLatencySilos > majorityThreshold)
        {
            // In a real implementation, we would coordinate with other silos
            // to determine which partition has quorum and should continue
            // For now, we just log this condition
        }
    }

    private void HealthCheckCallback(object? state)
    {
        if (!_isRunning)
            return;

        try
        {
            PerformHealthCheckAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Log error
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
    }
}
