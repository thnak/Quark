using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents driver state.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record DriverState(
    [property: ProtoMember(1)] string DriverId,
    [property: ProtoMember(2)] string Name,
    [property: ProtoMember(3)] DriverStatus Status,
    [property: ProtoMember(4)] GpsLocation? CurrentLocation = null,
    [property: ProtoMember(5)] string? CurrentOrderId = null,
    [property: ProtoMember(6)] DateTime LastUpdated = default,
    [property: ProtoMember(7)] int DeliveredToday = 0);