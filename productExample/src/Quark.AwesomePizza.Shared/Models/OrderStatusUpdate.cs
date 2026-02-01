namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Order status update event for streaming.
/// </summary>
public record OrderStatusUpdate(
    string OrderId,
    OrderStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null,
    string? Message = null);