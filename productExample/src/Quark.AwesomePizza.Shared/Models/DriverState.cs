namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents driver state.
/// </summary>
public record DriverState(
    string DriverId,
    string Name,
    DriverStatus Status,
    GpsLocation? CurrentLocation = null,
    string? CurrentOrderId = null,
    DateTime LastUpdated = default,
    int DeliveredToday = 0);