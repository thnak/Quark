using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents a pizza item in an order.
/// </summary>
[ProtoContract]
public record PizzaItem
{
    [ProtoMember(1)]
    public string PizzaType { get; set; } = "";
    
    [ProtoMember(2)]
    public string Size { get; set; } = "";
    
    [ProtoMember(3)]
    public List<string> Toppings { get; set; } = new();
    
    [ProtoMember(4)]
    public int Quantity { get; set; }
    
    [ProtoMember(5)]
    public decimal Price { get; set; }
}