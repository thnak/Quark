namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Chef work assignment.
/// </summary>
public record ChefAssignment(
    string ChefId,
    string OrderId,
    string PizzaType,
    DateTime AssignedAt);