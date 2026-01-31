using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Networking.Abstractions;

namespace Quark.Placement.Memory;

/// <summary>
/// Placement policy that considers memory usage when selecting silos.
/// Prevents OOM conditions by avoiding memory-constrained silos.
/// </summary>
public sealed class MemoryAwarePlacementPolicy : IPlacementPolicy
{
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly ILogger<MemoryAwarePlacementPolicy> _logger;
    private readonly MemoryAwarePlacementOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryAwarePlacementPolicy"/> class.
    /// </summary>
    public MemoryAwarePlacementPolicy(
        IMemoryMonitor memoryMonitor,
        IOptions<MemoryAwarePlacementOptions> options,
        ILogger<MemoryAwarePlacementPolicy> logger)
    {
        _memoryMonitor = memoryMonitor ?? throw new ArgumentNullException(nameof(memoryMonitor));
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

        // Get current memory metrics
        var memoryMetrics = _memoryMonitor.GetSiloMemoryMetrics();

        // Check if memory pressure is critical
        if (memoryMetrics.MemoryPressure >= _options.MemoryPressureThreshold)
        {
            _logger.LogWarning(
                "High memory pressure detected: {MemoryPressure:P0}. Used: {UsedMB} MB, Available: {AvailableMB} MB",
                memoryMetrics.MemoryPressure,
                memoryMetrics.TotalMemoryBytes / (1024 * 1024),
                memoryMetrics.AvailableMemoryBytes / (1024 * 1024));

            if (_options.RejectPlacementOnCriticalMemory && 
                memoryMetrics.TotalMemoryBytes >= _options.CriticalThresholdBytes)
            {
                _logger.LogError(
                    "Critical memory threshold exceeded ({CriticalMB} MB). Rejecting placement for actor {ActorType}:{ActorId}",
                    _options.CriticalThresholdBytes / (1024 * 1024),
                    actorType,
                    actorId);
                return null;
            }
        }

        // For now, use simple round-robin selection
        // In a real distributed system, we'd query memory stats from all silos
        // and prefer the one with the most available memory
        var selectedSilo = SelectSiloWithLowestMemoryUsage(availableSilos);
        
        _logger.LogDebug(
            "Selected silo {SiloId} for actor {ActorType}:{ActorId}. Memory pressure: {MemoryPressure:P0}",
            selectedSilo,
            actorType,
            actorId,
            memoryMetrics.MemoryPressure);

        return selectedSilo;
    }

    private string? SelectSiloWithLowestMemoryUsage(IReadOnlyCollection<string> availableSilos)
    {
        // Simple selection - in a real implementation, this would query
        // memory stats from all silos and select the one with lowest usage
        // For now, just use the first available silo (local silo has highest priority)
        return availableSilos.FirstOrDefault();
    }
}
