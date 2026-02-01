namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen queue entry.
/// </summary>
public record KitchenQueueItem(
    string OrderId,
    List<PizzaItem> Items,
    DateTime OrderTime,
    string? AssignedChefId = null,
    DateTime? EstimatedCompletionTime = null);