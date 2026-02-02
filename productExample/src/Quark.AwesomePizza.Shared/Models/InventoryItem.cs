using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Inventory item.
/// </summary>
[ProtoContract]
public record InventoryItem(
    [property: ProtoMember(1)] string ItemId,
    [property: ProtoMember(2)] string Name,
    [property: ProtoMember(3)] decimal Quantity,
    [property: ProtoMember(4)] string Unit,
    [property: ProtoMember(5)] decimal LowStockThreshold,
    [property: ProtoMember(6)] decimal ReorderQuantity);