using Quark.Abstractions;
using Quark.Abstractions.Converters;
using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Shared.Converters;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Driver Actor - manages delivery driver state and location.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IDriverActor : IQuarkActor
{
    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "name", Order = 0)]
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState> InitializeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current driver state.
    /// </summary>
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver location from MQTT telemetry.
    /// </summary>
    [BinaryConverter(typeof(DoubleConverter), ParameterName = "latitude", Order = 0)]
    [BinaryConverter(typeof(DoubleConverter), ParameterName = "longitude", Order = 1)]
    [BinaryConverter(typeof(DateTimeConverter), ParameterName = "timestamp", Order = 2)]
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState> UpdateLocationAsync(double latitude, double longitude, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver status.
    /// </summary>
    [BinaryConverter(typeof(Int32Converter), ParameterName = "status", Order = 0)]
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState> UpdateStatusAsync(DriverStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an order to the driver.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "orderId", Order = 0)]
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the current delivery.
    /// </summary>
    [BinaryConverter(typeof(DriverStateConverter))]
    Task<DriverState> CompleteDeliveryAsync(CancellationToken cancellationToken = default);
}
