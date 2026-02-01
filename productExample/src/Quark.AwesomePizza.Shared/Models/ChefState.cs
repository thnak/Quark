namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents chef state.
/// </summary>
public record ChefState(
    string ChefId,
    string Name,
    ChefStatus Status,
    List<string> CurrentOrders,
    int CompletedToday = 0);