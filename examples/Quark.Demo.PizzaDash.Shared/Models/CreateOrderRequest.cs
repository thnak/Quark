namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Request to create a new order.
/// </summary>
public record CreateOrderRequest(
    string CustomerId,
    string PizzaType);