namespace Quark.Examples.PizzaTracker.Shared.Models;

/// <summary>
/// Represents the status of a pizza order.
/// </summary>
public enum PizzaStatus
{
    Ordered,
    Preparing,
    Baking,
    OutForDelivery,
    Delivered
}

/// <summary>
/// Represents a GPS location.
/// </summary>
public record GpsLocation(double Latitude, double Longitude, DateTime Timestamp);

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

/// <summary>
/// Represents a status update event for server-sent events.
/// </summary>
public record PizzaStatusUpdate(
    string OrderId,
    PizzaStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null);
