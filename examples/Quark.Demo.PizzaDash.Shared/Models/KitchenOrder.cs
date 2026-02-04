namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Represents a kitchen order for chef processing.
/// </summary>
public record KitchenOrder(
    string OrderId,
    string PizzaType,
    DateTime OrderTime);