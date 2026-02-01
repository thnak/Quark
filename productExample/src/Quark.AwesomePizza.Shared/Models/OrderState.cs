namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents the complete state of an order.
/// </summary>
public record OrderState(
    string OrderId,
    string CustomerId,
    string RestaurantId,
    List<PizzaItem> Items,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime LastUpdated,
    DateTime? EstimatedDeliveryTime = null,
    string? AssignedChefId = null,
    string? AssignedDriverId = null,
    GpsLocation? DeliveryAddress = null,
    GpsLocation? CurrentDriverLocation = null,
    decimal TotalAmount = 0,
    string? SpecialInstructions = null,
    string? ETag = null);