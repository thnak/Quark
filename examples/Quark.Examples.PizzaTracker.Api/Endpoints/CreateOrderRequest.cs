namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Request to create a new pizza order.
/// </summary>
public record CreateOrderRequest(string CustomerId, string PizzaType);