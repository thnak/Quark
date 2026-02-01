using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Chef Actor - manages individual chef workload.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IChefActor
{
    /// <summary>
    /// Initializes a new chef.
    /// </summary>
    Task<ChefState> InitializeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current chef state.
    /// </summary>
    Task<ChefState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an order to the chef.
    /// </summary>
    Task<ChefState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the current order as completed.
    /// </summary>
    Task<ChefState> CompleteOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates chef status.
    /// </summary>
    Task<ChefState> UpdateStatusAsync(ChefStatus status, CancellationToken cancellationToken = default);
}
