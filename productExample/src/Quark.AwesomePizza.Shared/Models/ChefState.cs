using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents chef state.
/// </summary>
[ProtoContract]
public record ChefState(
    [property: ProtoMember(1)] string ChefId,
    [property: ProtoMember(2)] string Name,
    [property: ProtoMember(3)] ChefStatus Status,
    [property: ProtoMember(4)] List<string> CurrentOrders,
    [property: ProtoMember(5)] int CompletedToday = 0);