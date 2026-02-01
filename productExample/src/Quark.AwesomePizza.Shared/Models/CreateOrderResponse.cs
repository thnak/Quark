namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Response after creating an order.
/// </summary>
public record CreateOrderResponse(
    string OrderId,
    OrderState State,
    DateTime EstimatedDeliveryTime);