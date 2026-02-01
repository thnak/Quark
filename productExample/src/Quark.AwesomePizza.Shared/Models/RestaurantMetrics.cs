namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Restaurant metrics.
/// </summary>
public record RestaurantMetrics(
    string RestaurantId,
    int ActiveOrders,
    int CompletedOrders,
    int AvailableDrivers,
    int BusyDrivers,
    int AvailableChefs,
    int BusyChefs,
    decimal AverageDeliveryTime,
    DateTime LastUpdated);