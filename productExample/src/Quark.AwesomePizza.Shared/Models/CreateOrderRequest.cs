using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to create a new order.
/// </summary>
[ProtoContract]
public record CreateOrderRequest
{
    [ProtoMember(1)]
    public string CustomerId { get; set; } = "";
    
    [ProtoMember(2)]
    public string RestaurantId { get; set; } = "";
    
    [ProtoMember(3)]
    public List<PizzaItem> Items { get; set; } = new();
    
    [ProtoMember(4)]
    public GpsLocation? DeliveryAddress { get; set; }
    
    [ProtoMember(5)]
    public string? SpecialInstructions { get; set; }
}