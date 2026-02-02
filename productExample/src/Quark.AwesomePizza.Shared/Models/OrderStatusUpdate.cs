using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Order status update event for streaming.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record OrderStatusUpdate(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] OrderStatus Status,
    [property: ProtoMember(3)] DateTime Timestamp,
    [property: ProtoMember(4)] GpsLocation? DriverLocation = null,
    [property: ProtoMember(5)] string? Message = null);