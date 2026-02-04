namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Represents a complete pizza order with all state.
/// </summary>
public record OrderState(
    string OrderId,
    string CustomerId,
    string PizzaType,
    OrderStatus Status,
    DateTime OrderTime,
    DateTime LastUpdated,
    string? DriverId = null,
    GpsLocation? DriverLocation = null,
    string? ETag = null);