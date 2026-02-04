using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Response for order creation.
/// </summary>
public record CreateOrderResponse(string OrderId, PizzaStatus Status, DateTime OrderTime);