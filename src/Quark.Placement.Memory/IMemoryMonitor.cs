namespace Quark.Placement.Memory;

/// <summary>
/// Monitors memory usage for actors and silos.
/// </summary>
public interface IMemoryMonitor
{
    /// <summary>
    /// Gets the estimated memory usage for a specific actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <returns>The estimated memory usage in bytes, or 0 if unknown.</returns>
    long GetActorMemoryUsage(string actorId);

    /// <summary>
    /// Gets the current memory metrics for the silo.
    /// </summary>
    /// <returns>The memory metrics.</returns>
    MemoryMetrics GetSiloMemoryMetrics();

    /// <summary>
    /// Gets the top memory-consuming actors.
    /// </summary>
    /// <param name="count">The number of actors to return.</param>
    /// <returns>A list of actor memory information.</returns>
    Task<IReadOnlyList<ActorMemoryInfo>> GetTopMemoryConsumersAsync(int count);

    /// <summary>
    /// Records memory usage for an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="memoryBytes">The memory usage in bytes.</param>
    void RecordActorMemoryUsage(string actorId, string actorType, long memoryBytes);
}
