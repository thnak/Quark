using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Abstractions.Clustering;

namespace Quark.Placement.Memory;

/// <summary>
/// Coordinates actor migration based on memory pressure.
/// Proactively moves actors from memory-constrained silos.
/// </summary>
public sealed class MemoryRebalancingCoordinator : IActorRebalancer
{
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IActorDirectory _actorDirectory;
    private readonly ILogger<MemoryRebalancingCoordinator> _logger;
    private readonly MemoryAwarePlacementOptions _options;
    private readonly Dictionary<string, DateTimeOffset> _lastMigrationTime = new();
    private readonly TimeSpan _migrationCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryRebalancingCoordinator"/> class.
    /// </summary>
    public MemoryRebalancingCoordinator(
        IMemoryMonitor memoryMonitor,
        IActorDirectory actorDirectory,
        IOptions<MemoryAwarePlacementOptions> options,
        ILogger<MemoryRebalancingCoordinator> logger)
    {
        _memoryMonitor = memoryMonitor ?? throw new ArgumentNullException(nameof(memoryMonitor));
        _actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<RebalancingDecision>> EvaluateRebalancingAsync(
        CancellationToken cancellationToken = default)
    {
        var decisions = new List<RebalancingDecision>();

        try
        {
            // Get current memory metrics
            var memoryMetrics = _memoryMonitor.GetSiloMemoryMetrics();

            // Only rebalance if memory pressure is high
            if (memoryMetrics.MemoryPressure < _options.MemoryPressureThreshold)
            {
                return decisions;
            }

            _logger.LogInformation(
                "High memory pressure detected: {MemoryPressure:P0}. Evaluating rebalancing.",
                memoryMetrics.MemoryPressure);

            // Get top memory-consuming actors
            var topConsumers = await _memoryMonitor.GetTopMemoryConsumersAsync(10);

            foreach (var actorInfo in topConsumers)
            {
                // Check migration cooldown
                if (_lastMigrationTime.TryGetValue(actorInfo.ActorId, out var lastMigration))
                {
                    if (DateTimeOffset.UtcNow - lastMigration < _migrationCooldown)
                    {
                        continue; // Skip actors that were recently migrated
                    }
                }

                // Find current location
                var location = await _actorDirectory.LookupActorAsync(
                    actorInfo.ActorId, 
                    actorInfo.ActorType, 
                    cancellationToken);

                if (location == null)
                {
                    continue;
                }

                // Calculate migration cost (based on memory size)
                var cost = await CalculateMigrationCostAsync(
                    actorInfo.ActorId, 
                    actorInfo.ActorType, 
                    cancellationToken);

                // Create rebalancing decision
                // Note: In a real implementation, we'd select a target silo with lower memory usage
                // For now, we just propose the migration without a specific target
                var decision = new RebalancingDecision(
                    actorInfo.ActorId,
                    actorInfo.ActorType,
                    location.SiloId,
                    location.SiloId, // TODO: Select actual target silo
                    RebalancingReason.HealthDegradation,
                    cost);

                decisions.Add(decision);

                // Limit to 5 migrations per evaluation to avoid overwhelming the system
                if (decisions.Count >= 5)
                {
                    break;
                }
            }

            _logger.LogInformation("Evaluated {Count} actor migrations for memory rebalancing", decisions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating memory-based rebalancing");
        }

        return decisions;
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteRebalancingAsync(
        RebalancingDecision decision,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Executing memory-based migration for actor {ActorType}:{ActorId} from {SourceSilo} to {TargetSilo}",
                decision.ActorType,
                decision.ActorId,
                decision.SourceSiloId,
                decision.TargetSiloId);

            // Unregister from source silo
            await _actorDirectory.UnregisterActorAsync(
                decision.ActorId, 
                decision.ActorType, 
                cancellationToken);

            // Register on target silo
            var newLocation = new ActorLocation(
                decision.ActorId,
                decision.ActorType,
                decision.TargetSiloId);

            await _actorDirectory.RegisterActorAsync(newLocation, cancellationToken);

            // Record migration time
            _lastMigrationTime[decision.ActorId] = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Successfully migrated actor {ActorType}:{ActorId} to reduce memory pressure",
                decision.ActorType,
                decision.ActorId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to migrate actor {ActorType}:{ActorId}",
                decision.ActorType,
                decision.ActorId);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<double> CalculateMigrationCostAsync(
        string actorId,
        string actorType,
        CancellationToken cancellationToken = default)
    {
        // Calculate cost based on actor memory footprint
        var memoryUsage = _memoryMonitor.GetActorMemoryUsage(actorId);
        
        if (memoryUsage == 0)
        {
            // Unknown memory usage, assume medium cost
            return Task.FromResult(0.5);
        }

        // Normalize to 0.0 - 1.0 scale
        // Larger actors have higher migration cost
        var costMB = memoryUsage / (1024.0 * 1024.0);
        var normalizedCost = Math.Min(costMB / 100.0, 1.0); // Cap at 100 MB = cost of 1.0

        return Task.FromResult(normalizedCost);
    }
}
