namespace Quark.Placement.Abstractions;

/// <summary>
/// Defines a strategy for NUMA-aware actor placement.
/// NUMA (Non-Uniform Memory Access) optimization co-locates related actors on the same NUMA node
/// to minimize memory access latency and maximize CPU cache efficiency.
/// </summary>
public interface INumaPlacementStrategy
{
    /// <summary>
    /// Gets the NUMA node that should host the specified actor.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preferred NUMA node ID (0-based), or null to use default placement.</returns>
    Task<int?> GetPreferredNumaNodeAsync(Type actorType, string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about available NUMA nodes in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of available NUMA node information.</returns>
    Task<IReadOnlyCollection<NumaNodeInfo>> GetAvailableNumaNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the strategy when an actor is activated on a specific NUMA node.
    /// This allows the strategy to track actor placement for affinity decisions.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="numaNode">The NUMA node where the actor was activated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnActorActivatedAsync(Type actorType, string actorId, int numaNode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the strategy when an actor is deactivated from a specific NUMA node.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="numaNode">The NUMA node where the actor was deactivated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnActorDeactivatedAsync(Type actorType, string actorId, int numaNode, CancellationToken cancellationToken = default);
}
