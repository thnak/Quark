using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Driver Actor - manages delivery driver state and location.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IDriverActorProxy : IQuarkActor
{
    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    Task<DriverState> InitializeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current driver state.
    /// </summary>
    Task<DriverState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver location from MQTT telemetry.
    /// </summary>
    Task<DriverState> UpdateLocationAsync(double latitude, double longitude, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver status.
    /// </summary>
    Task<DriverState> UpdateStatusAsync(DriverStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an order to the driver.
    /// </summary>
    Task<DriverState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the current delivery.
    /// </summary>
    Task<DriverState> CompleteDeliveryAsync(CancellationToken cancellationToken = default);
}
