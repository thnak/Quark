namespace Quark.Profiling.Abstractions;

/// <summary>
/// Provides profiling capabilities for individual actors.
/// Tracks per-actor CPU, memory, and method latency.
/// </summary>
public interface IActorProfiler
{
    /// <summary>
    /// Starts profiling an actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    void StartProfiling(string actorType, string actorId);

    /// <summary>
    /// Stops profiling an actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    void StopProfiling(string actorType, string actorId);

    /// <summary>
    /// Records a method invocation for an actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="methodName">The method name.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    void RecordMethodInvocation(string actorType, string actorId, string methodName, double durationMs);

    /// <summary>
    /// Records memory allocation for an actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="bytes">The number of bytes allocated.</param>
    void RecordAllocation(string actorType, string actorId, long bytes);

    /// <summary>
    /// Gets profiling data for a specific actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    /// <returns>Actor profiling data if found.</returns>
    ActorProfilingData? GetProfilingData(string actorType, string actorId);

    /// <summary>
    /// Gets profiling data for all actors of a specific type.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <returns>Collection of actor profiling data.</returns>
    IEnumerable<ActorProfilingData> GetProfilingDataByType(string actorType);

    /// <summary>
    /// Gets profiling data for all actors.
    /// </summary>
    /// <returns>Collection of actor profiling data.</returns>
    IEnumerable<ActorProfilingData> GetAllProfilingData();

    /// <summary>
    /// Clears profiling data for a specific actor.
    /// </summary>
    /// <param name="actorType">The type name of the actor.</param>
    /// <param name="actorId">The actor identifier.</param>
    void ClearProfilingData(string actorType, string actorId);

    /// <summary>
    /// Clears all profiling data.
    /// </summary>
    void ClearAllProfilingData();
}
