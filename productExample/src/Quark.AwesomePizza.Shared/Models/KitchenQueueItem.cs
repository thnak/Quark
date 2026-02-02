using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen queue entry.
/// </summary>
[ProtoContract]
public record KitchenQueueItem(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] List<PizzaItem> Items,
    [property: ProtoMember(3)] DateTime OrderTime,
    [property: ProtoMember(4)] string? AssignedChefId = null,
    [property: ProtoMember(5)] DateTime? EstimatedCompletionTime = null);