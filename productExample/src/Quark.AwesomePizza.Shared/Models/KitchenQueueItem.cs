using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Kitchen queue entry.
/// </summary>
[ProtoContract]
public record KitchenQueueItem
{
    [ProtoMember(1)]
    public string OrderId { get; set; } = "";
    
    [ProtoMember(2)]
    public List<PizzaItem> Items { get; set; } = new();
    
    [ProtoMember(3)]
    public DateTime OrderTime { get; set; }
    
    [ProtoMember(4)]
    public string? AssignedChefId { get; set; }
    
    [ProtoMember(5)]
    public DateTime? EstimatedCompletionTime { get; set; }
}