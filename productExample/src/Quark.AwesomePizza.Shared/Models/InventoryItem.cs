namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Inventory item.
/// </summary>
public record InventoryItem(
    string ItemId,
    string Name,
    decimal Quantity,
    string Unit,
    decimal LowStockThreshold,
    decimal ReorderQuantity);