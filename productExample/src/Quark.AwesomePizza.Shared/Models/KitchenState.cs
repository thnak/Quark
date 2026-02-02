using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen state.
/// </summary>
[ProtoContract]
public record KitchenState
{
    [ProtoMember(1)]
    public string KitchenId { get; set; } = "";
    
    [ProtoMember(2)]
    public string RestaurantId { get; set; } = "";
    
    [ProtoMember(3)]
    public List<KitchenQueueItem> Queue { get; set; } = new();
    
    [ProtoMember(4)]
    public List<string> AvailableChefs { get; set; } = new();
    
    [ProtoMember(5)]
    public int OrdersCompletedToday { get; set; }
}