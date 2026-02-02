using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Response after creating an order.
/// </summary>
[ProtoContract]
public record CreateOrderResponse
{
    [ProtoMember(1)]
    public string OrderId { get; set; } = "";
    
    [ProtoMember(2)]
    public OrderState? State { get; set; }
    
    [ProtoMember(3)]
    public DateTime EstimatedDeliveryTime { get; set; }
}