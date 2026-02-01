namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen state.
/// </summary>
public record KitchenState(
    string KitchenId,
    string RestaurantId,
    List<KitchenQueueItem> Queue,
    List<string> AvailableChefs,
    int OrdersCompletedToday = 0);