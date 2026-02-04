namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Status update event for streaming.
/// </summary>
public record OrderStatusUpdate(
    string OrderId,
    OrderStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null);