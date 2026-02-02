using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Inventory state for a restaurant.
/// </summary>
[ProtoContract]
public record InventoryState(
    [property: ProtoMember(1)] string RestaurantId,
    [property: ProtoMember(2)] Dictionary<string, InventoryItem> Items,
    [property: ProtoMember(3)] DateTime LastUpdated);