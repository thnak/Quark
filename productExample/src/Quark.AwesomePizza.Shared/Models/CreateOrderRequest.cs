namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to create a new order.
/// </summary>
public record CreateOrderRequest(
    string CustomerId,
    string RestaurantId,
    List<PizzaItem> Items,
    GpsLocation DeliveryAddress,
    string? SpecialInstructions = null);