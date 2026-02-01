namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to update order status.
/// </summary>
public record UpdateStatusRequest(
    OrderStatus NewStatus,
    string? AssignedChefId = null,
    string? AssignedDriverId = null);