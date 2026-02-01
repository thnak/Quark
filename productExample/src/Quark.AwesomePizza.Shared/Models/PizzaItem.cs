namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents a pizza item in an order.
/// </summary>
public record PizzaItem(
    string PizzaType,
    string Size,
    List<string> Toppings,
    int Quantity,
    decimal Price);