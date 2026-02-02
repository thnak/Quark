using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents a pizza item in an order.
/// </summary>
[ProtoContract]
public record PizzaItem(
    [property: ProtoMember(1)] string PizzaType,
    [property: ProtoMember(2)] string Size,
    [property: ProtoMember(3)] List<string> Toppings,
    [property: ProtoMember(4)] int Quantity,
    [property: ProtoMember(5)] decimal Price);