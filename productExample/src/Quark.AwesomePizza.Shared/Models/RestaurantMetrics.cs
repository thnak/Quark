using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Restaurant metrics.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record RestaurantMetrics(
    [property: ProtoMember(1)] string RestaurantId,
    [property: ProtoMember(2)] int ActiveOrders,
    [property: ProtoMember(3)] int CompletedOrders,
    [property: ProtoMember(4)] int AvailableDrivers,
    [property: ProtoMember(5)] int BusyDrivers,
    [property: ProtoMember(6)] int AvailableChefs,
    [property: ProtoMember(7)] int BusyChefs,
    [property: ProtoMember(8)] decimal AverageDeliveryTime,
    [property: ProtoMember(9)] DateTime LastUpdated);