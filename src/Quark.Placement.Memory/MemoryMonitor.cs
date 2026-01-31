using System.Collections.Concurrent;

namespace Quark.Placement.Memory;

/// <summary>
/// Default implementation of <see cref="IMemoryMonitor"/>.
/// Tracks actor memory usage and system memory metrics.
/// </summary>
public sealed class MemoryMonitor : IMemoryMonitor
{
    private readonly ConcurrentDictionary<string, (string ActorType, long MemoryBytes, DateTimeOffset Timestamp)> _actorMemory = new();

    /// <inheritdoc />
    public long GetActorMemoryUsage(string actorId)
    {
        if (_actorMemory.TryGetValue(actorId, out var info))
        {
            return info.MemoryBytes;
        }
        return 0;
    }

    /// <inheritdoc />
    public MemoryMetrics GetSiloMemoryMetrics()
    {
        var totalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var gcMemInfo = GC.GetGCMemoryInfo();
        
        return new MemoryMetrics
        {
            TotalMemoryBytes = totalMemory,
            AvailableMemoryBytes = Math.Max(0, gcMemInfo.TotalAvailableMemoryBytes - totalMemory),
            MemoryPressure = CalculateMemoryPressure(totalMemory, gcMemInfo.TotalAvailableMemoryBytes),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            LastGCPause = TimeSpan.Zero, // GC pause tracking requires specialized metrics
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActorMemoryInfo>> GetTopMemoryConsumersAsync(int count)
    {
        var topConsumers = _actorMemory
            .Select(kvp => new ActorMemoryInfo(kvp.Key, kvp.Value.ActorType, kvp.Value.MemoryBytes)
            {
                Timestamp = kvp.Value.Timestamp
            })
            .OrderByDescending(info => info.MemoryBytes)
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<ActorMemoryInfo>>(topConsumers);
    }

    /// <inheritdoc />
    public void RecordActorMemoryUsage(string actorId, string actorType, long memoryBytes)
    {
        _actorMemory[actorId] = (actorType, memoryBytes, DateTimeOffset.UtcNow);
    }

    private double CalculateMemoryPressure(long usedMemory, long totalMemory)
    {
        if (totalMemory <= 0)
            return 0.0;

        var usage = (double)usedMemory / totalMemory;
        return Math.Clamp(usage, 0.0, 1.0);
    }
}
