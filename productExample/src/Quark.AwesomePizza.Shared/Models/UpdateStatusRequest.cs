using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to update order status.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record UpdateStatusRequest(
    [property: ProtoMember(1)] OrderStatus NewStatus,
    [property: ProtoMember(2)] string? AssignedChefId = null,
    [property: ProtoMember(3)] string? AssignedDriverId = null);