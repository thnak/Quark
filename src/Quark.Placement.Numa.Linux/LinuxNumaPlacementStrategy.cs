using System.Diagnostics;
using Quark.Placement.Abstractions;

namespace Quark.Placement.Numa.Linux;

/// <summary>
/// Linux-specific NUMA placement strategy.
/// Uses /sys/devices/system/node/ to detect NUMA topology.
/// </summary>
public sealed class LinuxNumaPlacementStrategy : NumaPlacementStrategyBase
{
    private readonly int _processorCount;
    private List<NumaNodeInfo>? _cachedNodes;
    private DateTime _lastCacheUpdate;
    private readonly NumaOptimizationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxNumaPlacementStrategy"/> class.
    /// </summary>
    /// <param name="options">Configuration options for NUMA optimization.</param>
    public LinuxNumaPlacementStrategy(NumaOptimizationOptions options) : base(options)
    {
        _processorCount = Environment.ProcessorCount;
        _options = options;
        _lastCacheUpdate = DateTime.MinValue;
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<NumaNodeInfo>> GetAvailableNumaNodesAsync(CancellationToken cancellationToken = default)
    {
        // Check cache validity
        var now = DateTime.UtcNow;
        if (_cachedNodes != null && 
            (now - _lastCacheUpdate).TotalSeconds < _options.MetricsRefreshIntervalSeconds)
        {
            return _cachedNodes;
        }

        var nodes = new List<NumaNodeInfo>();

        // Try to detect NUMA nodes from /sys
        if (Directory.Exists("/sys/devices/system/node"))
        {
            var nodeDirs = Directory.GetDirectories("/sys/devices/system/node", "node*")
                .Where(d => int.TryParse(Path.GetFileName(d).Substring(4), out _))
                .OrderBy(d => d)
                .ToList();

            foreach (var nodeDir in nodeDirs)
            {
                var nodeIdStr = Path.GetFileName(nodeDir).Substring(4);
                if (!int.TryParse(nodeIdStr, out var nodeId))
                    continue;

                var nodeInfo = await ReadNumaNodeInfoAsync(nodeId, nodeDir, cancellationToken);
                nodes.Add(nodeInfo);
            }
        }

        // Fallback: If no NUMA nodes detected, create a single virtual node
        if (nodes.Count == 0)
        {
            nodes.Add(new NumaNodeInfo
            {
                NodeId = 0,
                ProcessorIds = Enumerable.Range(0, _processorCount).ToList(),
                MemoryCapacityBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - GC.GetTotalMemory(false),
                CpuUtilizationPercent = 0,
                ActiveActorCount = 0
            });
        }

        _cachedNodes = nodes;
        _lastCacheUpdate = now;

        return nodes;
    }

    private async Task<NumaNodeInfo> ReadNumaNodeInfoAsync(int nodeId, string nodeDir, CancellationToken cancellationToken)
    {
        var processorIds = await ReadProcessorIdsAsync(nodeDir, cancellationToken);
        var (memTotal, memAvail) = await ReadMemoryInfoAsync(nodeId, cancellationToken);
        var cpuUtil = await ReadCpuUtilizationAsync(processorIds, cancellationToken);

        return new NumaNodeInfo
        {
            NodeId = nodeId,
            ProcessorIds = processorIds,
            MemoryCapacityBytes = memTotal,
            AvailableMemoryBytes = memAvail,
            CpuUtilizationPercent = cpuUtil,
            ActiveActorCount = 0
        };
    }

    private async Task<List<int>> ReadProcessorIdsAsync(string nodeDir, CancellationToken cancellationToken)
    {
        var cpuListPath = Path.Combine(nodeDir, "cpulist");
        if (!File.Exists(cpuListPath))
            return new List<int>();

        try
        {
            var cpuListStr = (await File.ReadAllTextAsync(cpuListPath, cancellationToken)).Trim();
            return ParseCpuList(cpuListStr);
        }
        catch
        {
            return new List<int>();
        }
    }

    private List<int> ParseCpuList(string cpuList)
    {
        var processors = new List<int>();
        var parts = cpuList.Split(',');

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (range.Length == 2 && 
                    int.TryParse(range[0], out var start) && 
                    int.TryParse(range[1], out var end))
                {
                    processors.AddRange(Enumerable.Range(start, end - start + 1));
                }
            }
            else if (int.TryParse(part, out var cpu))
            {
                processors.Add(cpu);
            }
        }

        return processors;
    }

    private async Task<(long Total, long Available)> ReadMemoryInfoAsync(int nodeId, CancellationToken cancellationToken)
    {
        try
        {
            var meminfoPath = $"/sys/devices/system/node/node{nodeId}/meminfo";
            if (!File.Exists(meminfoPath))
                return (0, 0);

            var lines = await File.ReadAllLinesAsync(meminfoPath, cancellationToken);
            long total = 0, free = 0;

            foreach (var line in lines)
            {
                if (line.Contains("MemTotal:"))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 && long.TryParse(parts[3], out var kb))
                        total = kb * 1024;
                }
                else if (line.Contains("MemFree:"))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 && long.TryParse(parts[3], out var kb))
                        free = kb * 1024;
                }
            }

            return (total, free);
        }
        catch
        {
            return (0, 0);
        }
    }

    private async Task<double> ReadCpuUtilizationAsync(List<int> processorIds, CancellationToken cancellationToken)
    {
        // Simplified CPU utilization - in production, this would use /proc/stat
        // and track per-CPU statistics over time
        await Task.CompletedTask;
        return 0.0;
    }
}
