namespace Quark.Examples.PizzaTracker.Shared.Models;

/// <summary>
/// Represents the status of a pizza order.
/// </summary>
public enum PizzaStatus
{
    Ordered,
    Preparing,
    Baking,
    OutForDelivery,
    Delivered
}