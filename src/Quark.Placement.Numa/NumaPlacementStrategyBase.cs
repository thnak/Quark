using System.Collections.Concurrent;
using Quark.Placement.Abstractions;

namespace Quark.Placement.Numa;

/// <summary>
/// Base implementation of NUMA-aware actor placement strategy.
/// This provides common logic for NUMA node selection and actor tracking.
/// Platform-specific implementations should inherit from this class.
/// </summary>
public abstract class NumaPlacementStrategyBase : INumaPlacementStrategy
{
    private readonly NumaOptimizationOptions _options;
    private readonly ConcurrentDictionary<string, int> _actorToNodeMap = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _affinityGroupToActors = new();
    private readonly ConcurrentDictionary<int, int> _nodeActorCounts = new();
    private int _nextNodeRoundRobin = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumaPlacementStrategyBase"/> class.
    /// </summary>
    /// <param name="options">Configuration options for NUMA optimization.</param>
    protected NumaPlacementStrategyBase(NumaOptimizationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public virtual async Task<int?> GetPreferredNumaNodeAsync(Type actorType, string actorId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        // Check if actor is already placed
        if (_actorToNodeMap.TryGetValue(actorId, out var existingNode))
            return existingNode;

        // Check affinity groups
        var affinityGroup = GetAffinityGroup(actorType.Name);
        if (affinityGroup != null && TryGetAffinityGroupNode(affinityGroup, out var affinityNode))
            return affinityNode;

        // Get available nodes
        var nodes = await GetAvailableNumaNodesAsync(cancellationToken);
        if (nodes.Count == 0)
            return null;

        // Select node based on strategy
        var selectedNode = _options.BalancedPlacement
            ? SelectLeastLoadedNode(nodes)
            : SelectNextAvailableNode(nodes);

        return selectedNode;
    }

    /// <inheritdoc/>
    public abstract Task<IReadOnlyCollection<NumaNodeInfo>> GetAvailableNumaNodesAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual Task OnActorActivatedAsync(Type actorType, string actorId, int numaNode, CancellationToken cancellationToken = default)
    {
        _actorToNodeMap[actorId] = numaNode;
        _nodeActorCounts.AddOrUpdate(numaNode, 1, (_, count) => count + 1);

        // Track affinity group
        var affinityGroup = GetAffinityGroup(actorType.Name);
        if (affinityGroup != null)
        {
            var actors = _affinityGroupToActors.GetOrAdd(affinityGroup, _ => new HashSet<string>());
            lock (actors)
            {
                actors.Add(actorId);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task OnActorDeactivatedAsync(Type actorType, string actorId, int numaNode, CancellationToken cancellationToken = default)
    {
        _actorToNodeMap.TryRemove(actorId, out _);
        _nodeActorCounts.AddOrUpdate(numaNode, 0, (_, count) => Math.Max(0, count - 1));

        // Remove from affinity group
        var affinityGroup = GetAffinityGroup(actorType.Name);
        if (affinityGroup != null && _affinityGroupToActors.TryGetValue(affinityGroup, out var actors))
        {
            lock (actors)
            {
                actors.Remove(actorId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the affinity group for the specified actor type.
    /// </summary>
    /// <param name="actorTypeName">Name of the actor type.</param>
    /// <returns>The affinity group name, or null if not in any group.</returns>
    protected string? GetAffinityGroup(string actorTypeName)
    {
        foreach (var (groupName, actorTypes) in _options.AffinityGroups)
        {
            if (actorTypes.Contains(actorTypeName))
                return groupName;
        }
        return null;
    }

    /// <summary>
    /// Tries to get the NUMA node for actors in the specified affinity group.
    /// </summary>
    /// <param name="affinityGroup">The affinity group name.</param>
    /// <param name="numaNode">The NUMA node if found.</param>
    /// <returns>True if a node was found for the affinity group.</returns>
    protected bool TryGetAffinityGroupNode(string affinityGroup, out int numaNode)
    {
        numaNode = -1;
        if (!_affinityGroupToActors.TryGetValue(affinityGroup, out var actors))
            return false;

        string? firstActor;
        lock (actors)
        {
            firstActor = actors.FirstOrDefault();
        }

        if (firstActor != null && _actorToNodeMap.TryGetValue(firstActor, out numaNode))
            return true;

        return false;
    }

    /// <summary>
    /// Selects the NUMA node with the least load (CPU and memory utilization).
    /// </summary>
    /// <param name="nodes">Available NUMA nodes.</param>
    /// <returns>The selected NUMA node ID.</returns>
    protected int SelectLeastLoadedNode(IReadOnlyCollection<NumaNodeInfo> nodes)
    {
        var availableNodes = nodes
            .Where(n => n.CpuUtilizationPercent < _options.NodeCpuThreshold * 100 &&
                       (n.MemoryCapacityBytes == 0 || 
                        (double)n.AvailableMemoryBytes / n.MemoryCapacityBytes > (1 - _options.NodeMemoryThreshold)))
            .ToList();

        if (availableNodes.Count == 0)
            availableNodes = nodes.ToList();

        // Select node with lowest combined score (CPU + memory + actor count)
        return availableNodes
            .OrderBy(n => n.CpuUtilizationPercent * 0.4 + 
                         (1 - (double)n.AvailableMemoryBytes / Math.Max(1, n.MemoryCapacityBytes)) * 0.4 +
                         n.ActiveActorCount * 0.2)
            .First()
            .NodeId;
    }

    /// <summary>
    /// Selects the next available NUMA node using round-robin.
    /// </summary>
    /// <param name="nodes">Available NUMA nodes.</param>
    /// <returns>The selected NUMA node ID.</returns>
    protected int SelectNextAvailableNode(IReadOnlyCollection<NumaNodeInfo> nodes)
    {
        var nodeList = nodes.OrderBy(n => n.NodeId).ToList();
        if (nodeList.Count == 0)
            return 0;

        var index = Interlocked.Increment(ref _nextNodeRoundRobin) % nodeList.Count;
        return nodeList[index].NodeId;
    }
}
