using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents chef state.
/// </summary>
[ProtoContract]
public record ChefState
{
    [ProtoMember(1)] public string ChefId { get; set; } = "";

    [ProtoMember(2)] public string Name { get; set; } = "";

    [ProtoMember(3)] public ChefStatus Status { get; set; }

    [ProtoMember(4)] public List<string> CurrentOrders { get; set; } = new();

    [ProtoMember(5)] public int CompletedToday { get; set; }
}