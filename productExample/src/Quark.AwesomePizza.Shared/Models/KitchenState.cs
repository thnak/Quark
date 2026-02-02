using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen state.
/// </summary>
[ProtoContract]
public record KitchenState(
    [property: ProtoMember(1)] string KitchenId,
    [property: ProtoMember(2)] string RestaurantId,
    [property: ProtoMember(3)] List<KitchenQueueItem> Queue,
    [property: ProtoMember(4)] List<string> AvailableChefs,
    [property: ProtoMember(5)] int OrdersCompletedToday = 0);