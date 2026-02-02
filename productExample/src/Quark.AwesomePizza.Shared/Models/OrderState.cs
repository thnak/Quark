using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents the complete state of an order.
/// </summary>
[ProtoContract]
public record OrderState
{
    [ProtoMember(1)]
    public string OrderId { get; set; } = "";
    
    [ProtoMember(2)]
    public string CustomerId { get; set; } = "";
    
    [ProtoMember(3)]
    public string RestaurantId { get; set; } = "";
    
    [ProtoMember(4)]
    public List<PizzaItem> Items { get; set; } = new();
    
    [ProtoMember(5)]
    public OrderStatus Status { get; set; }
    
    [ProtoMember(6)]
    public DateTime CreatedAt { get; set; }
    
    [ProtoMember(7)]
    public DateTime LastUpdated { get; set; }
    
    [ProtoMember(8)]
    public DateTime? EstimatedDeliveryTime { get; set; }
    
    [ProtoMember(9)]
    public string? AssignedChefId { get; set; }
    
    [ProtoMember(10)]
    public string? AssignedDriverId { get; set; }
    
    [ProtoMember(11)]
    public GpsLocation? DeliveryAddress { get; set; }
    
    [ProtoMember(12)]
    public GpsLocation? CurrentDriverLocation { get; set; }
    
    [ProtoMember(13)]
    public decimal TotalAmount { get; set; }
    
    [ProtoMember(14)]
    public string? SpecialInstructions { get; set; }
    
    [ProtoMember(15)]
    public string? ETag { get; set; }
}