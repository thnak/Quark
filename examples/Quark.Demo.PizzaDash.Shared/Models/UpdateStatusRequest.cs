namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Request to update order status.
/// </summary>
public record UpdateStatusRequest(
    OrderStatus NewStatus,
    string? DriverId = null);