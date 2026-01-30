using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;

namespace Quark.Clustering.Redis;

/// <summary>
/// Load-based actor rebalancer that migrates actors to balance load across silos.
/// </summary>
public sealed class LoadBasedRebalancer : IActorRebalancer
{
    private readonly IClusterHealthMonitor _healthMonitor;
    private readonly IActorDirectory _actorDirectory;
    private readonly IQuarkClusterMembership _clusterMembership;
    private readonly RebalancingOptions _options;
    private readonly ILogger<LoadBasedRebalancer> _logger;
    private readonly Dictionary<string, DateTimeOffset> _migrationCooldowns;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadBasedRebalancer"/> class.
    /// </summary>
    public LoadBasedRebalancer(
        IClusterHealthMonitor healthMonitor,
        IActorDirectory actorDirectory,
        IQuarkClusterMembership clusterMembership,
        IOptions<RebalancingOptions> options,
        ILogger<LoadBasedRebalancer> logger)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _migrationCooldowns = new Dictionary<string, DateTimeOffset>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<RebalancingDecision>> EvaluateRebalancingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<RebalancingDecision>();
        }

        try
        {
            var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
            if (silos.Count < 2)
            {
                // Need at least 2 silos to rebalance
                return Array.Empty<RebalancingDecision>();
            }

            // Get health scores for all silos
            var healthScores = new Dictionary<string, SiloHealthScore>();
            foreach (var silo in silos)
            {
                var health = await _healthMonitor.GetHealthScoreAsync(silo.SiloId, cancellationToken);
                if (health != null)
                {
                    healthScores[silo.SiloId] = health;
                }
            }

            if (healthScores.Count < 2)
            {
                return Array.Empty<RebalancingDecision>();
            }

            // Calculate load scores (inverse of health score - higher means more loaded)
            var loadScores = healthScores.ToDictionary(
                kvp => kvp.Key,
                kvp => 1.0 - kvp.Value.OverallScore);

            var avgLoad = loadScores.Values.Average();
            var maxLoad = loadScores.Values.Max();
            var minLoad = loadScores.Values.Min();

            // Check if rebalancing is needed
            if (maxLoad - minLoad < _options.LoadImbalanceThreshold)
            {
                _logger.LogDebug(
                    "Load is balanced across silos (max: {MaxLoad:F2}, min: {MinLoad:F2}, threshold: {Threshold:F2})",
                    maxLoad, minLoad, _options.LoadImbalanceThreshold);
                return Array.Empty<RebalancingDecision>();
            }

            _logger.LogInformation(
                "Load imbalance detected (max: {MaxLoad:F2}, min: {MinLoad:F2}), evaluating rebalancing",
                maxLoad, minLoad);

            // Find overloaded and underloaded silos
            var overloadedSilos = loadScores
                .Where(kvp => kvp.Value > avgLoad + _options.LoadImbalanceThreshold / 2)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            var underloadedSilos = loadScores
                .Where(kvp => kvp.Value < avgLoad - _options.LoadImbalanceThreshold / 2)
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            if (overloadedSilos.Count == 0 || underloadedSilos.Count == 0)
            {
                return Array.Empty<RebalancingDecision>();
            }

            // Generate rebalancing decisions
            var decisions = new List<RebalancingDecision>();
            var now = DateTimeOffset.UtcNow;

            foreach (var sourceSilo in overloadedSilos)
            {
                if (decisions.Count >= _options.MaxMigrationsPerCycle)
                {
                    break;
                }

                // Get actors on the overloaded silo
                var actors = await _actorDirectory.GetActorsBySiloAsync(sourceSilo, cancellationToken);
                
                foreach (var actor in actors.OrderBy(_ => Guid.NewGuid())) // Randomize to avoid bias
                {
                    if (decisions.Count >= _options.MaxMigrationsPerCycle)
                    {
                        break;
                    }

                    // Check cooldown
                    var actorKey = $"{actor.ActorType}:{actor.ActorId}";
                    lock (_lock)
                    {
                        if (_migrationCooldowns.TryGetValue(actorKey, out var lastMigration))
                        {
                            if (now - lastMigration < _options.MigrationCooldown)
                            {
                                continue; // Still in cooldown
                            }
                        }
                    }

                    // Calculate migration cost
                    var cost = await CalculateMigrationCostAsync(
                        actor.ActorId, actor.ActorType, cancellationToken);

                    if (cost > _options.MaxMigrationCost)
                    {
                        _logger.LogDebug(
                            "Skipping actor {ActorId} ({ActorType}) migration: cost {Cost:F2} exceeds limit {MaxCost:F2}",
                            actor.ActorId, actor.ActorType, cost, _options.MaxMigrationCost);
                        continue;
                    }

                    // Select best target silo (least loaded)
                    var targetSilo = underloadedSilos.FirstOrDefault();
                    if (targetSilo == null)
                    {
                        break;
                    }

                    var decision = new RebalancingDecision(
                        actor.ActorId,
                        actor.ActorType,
                        sourceSilo,
                        targetSilo,
                        RebalancingReason.LoadImbalance,
                        cost);

                    decisions.Add(decision);

                    _logger.LogInformation(
                        "Planned migration: {ActorId} ({ActorType}) from {Source} to {Target} (cost: {Cost:F2})",
                        actor.ActorId, actor.ActorType, sourceSilo, targetSilo, cost);
                }
            }

            return decisions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating rebalancing");
            return Array.Empty<RebalancingDecision>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteRebalancingAsync(
        RebalancingDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (decision == null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        try
        {
            _logger.LogInformation(
                "Executing migration: {ActorId} ({ActorType}) from {Source} to {Target}",
                decision.ActorId, decision.ActorType, decision.SourceSiloId, decision.TargetSiloId);

            // Unregister from old silo
            await _actorDirectory.UnregisterActorAsync(
                decision.ActorId, decision.ActorType, cancellationToken);

            // Register on new silo
            var newLocation = new ActorLocation(
                decision.ActorId,
                decision.ActorType,
                decision.TargetSiloId);

            await _actorDirectory.RegisterActorAsync(newLocation, cancellationToken);

            // Update cooldown
            var actorKey = $"{decision.ActorType}:{decision.ActorId}";
            lock (_lock)
            {
                _migrationCooldowns[actorKey] = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation(
                "Migration completed: {ActorId} ({ActorType}) now on {Target}",
                decision.ActorId, decision.ActorType, decision.TargetSiloId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute migration: {ActorId} ({ActorType})",
                decision.ActorId, decision.ActorType);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<double> CalculateMigrationCostAsync(
        string actorId,
        string actorType,
        CancellationToken cancellationToken = default)
    {
        // Migration cost calculation factors:
        // 1. State size (weighted by StateSizeWeight)
        // 2. Activation time (weighted by ActivationTimeWeight)
        // 3. Message queue depth (weighted by MessageQueueWeight)

        // For now, use a simple heuristic:
        // - Assume state size is proportional to actor ID hash (0.0-1.0)
        // - Assume activation time is constant (0.5)
        // - Assume message queue is empty (0.0)

        var stateHash = Math.Abs(actorId.GetHashCode()) / (double)int.MaxValue;
        var stateCost = stateHash * _options.StateSizeWeight;
        var activationCost = 0.5 * _options.ActivationTimeWeight;
        var queueCost = 0.0 * _options.MessageQueueWeight;

        var totalCost = stateCost + activationCost + queueCost;

        return Task.FromResult(Math.Min(1.0, totalCost));
    }
}
