using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents the complete state of an order.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record OrderState(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] string CustomerId,
    [property: ProtoMember(3)] string RestaurantId,
    [property: ProtoMember(4)] List<PizzaItem> Items,
    [property: ProtoMember(5)] OrderStatus Status,
    [property: ProtoMember(6)] DateTime CreatedAt,
    [property: ProtoMember(7)] DateTime LastUpdated,
    [property: ProtoMember(8)] DateTime? EstimatedDeliveryTime = null,
    [property: ProtoMember(9)] string? AssignedChefId = null,
    [property: ProtoMember(10)] string? AssignedDriverId = null,
    [property: ProtoMember(11)] GpsLocation? DeliveryAddress = null,
    [property: ProtoMember(12)] GpsLocation? CurrentDriverLocation = null,
    [property: ProtoMember(13)] decimal TotalAmount = 0,
    [property: ProtoMember(14)] string? SpecialInstructions = null,
    [property: ProtoMember(15)] string? ETag = null);