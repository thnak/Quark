namespace Quark.Placement.Abstractions;

/// <summary>
/// Defines a strategy for GPU-affinity actor placement.
/// Enables actors that perform compute-intensive operations (AI/ML, scientific computing)
/// to be placed with affinity to specific GPU devices for optimal performance.
/// </summary>
public interface IGpuPlacementStrategy
{
    /// <summary>
    /// Gets the GPU device that should be used for the specified actor.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preferred GPU device ID, or null to use CPU or default GPU.</returns>
    Task<int?> GetPreferredGpuDeviceAsync(Type actorType, string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about available GPU devices in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of available GPU device information.</returns>
    Task<IReadOnlyCollection<GpuDeviceInfo>> GetAvailableGpuDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the strategy when an actor is activated with a specific GPU device.
    /// This allows the strategy to track GPU utilization for placement decisions.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="gpuDeviceId">The GPU device ID assigned to the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnActorActivatedAsync(Type actorType, string actorId, int gpuDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the strategy when an actor is deactivated from a specific GPU device.
    /// </summary>
    /// <param name="actorType">The type of the actor.</param>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="gpuDeviceId">The GPU device ID that was assigned to the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnActorDeactivatedAsync(Type actorType, string actorId, int gpuDeviceId, CancellationToken cancellationToken = default);
}
