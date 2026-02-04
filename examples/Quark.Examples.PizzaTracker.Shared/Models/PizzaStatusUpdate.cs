namespace Quark.Examples.PizzaTracker.Shared.Models;

/// <summary>
/// Represents a status update event for server-sent events.
/// </summary>
public record PizzaStatusUpdate(
    string OrderId,
    PizzaStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null);