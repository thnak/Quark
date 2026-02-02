using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Response after creating an order.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record CreateOrderResponse(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] OrderState State,
    [property: ProtoMember(3)] DateTime EstimatedDeliveryTime);