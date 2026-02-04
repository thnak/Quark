namespace Quark.Examples.PizzaTracker.Shared.Models;

/// <summary>
/// Represents a pizza order.
/// </summary>
public record PizzaOrder(
    string OrderId,
    string CustomerId,
    string PizzaType,
    PizzaStatus Status,
    DateTime OrderTime,
    string? DriverId = null,
    GpsLocation? DriverLocation = null);