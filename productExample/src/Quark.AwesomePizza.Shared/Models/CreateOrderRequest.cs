using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to create a new order.
/// </summary>
[ProtoContract]
public record CreateOrderRequest(
    [property: ProtoMember(1)] string CustomerId,
    [property: ProtoMember(2)] string RestaurantId,
    [property: ProtoMember(3)] List<PizzaItem> Items,
    [property: ProtoMember(4)] GpsLocation DeliveryAddress,
    [property: ProtoMember(5)] string? SpecialInstructions = null);