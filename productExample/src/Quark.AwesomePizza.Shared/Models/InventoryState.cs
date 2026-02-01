namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Inventory state for a restaurant.
/// </summary>
public record InventoryState(
    string RestaurantId,
    Dictionary<string, InventoryItem> Items,
    DateTime LastUpdated);