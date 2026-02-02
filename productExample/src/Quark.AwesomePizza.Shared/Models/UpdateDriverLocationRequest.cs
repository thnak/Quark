using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to update driver location via MQTT.
/// </summary>
[ProtoContract]
public record UpdateDriverLocationRequest(
    [property: ProtoMember(1)] string DriverId,
    [property: ProtoMember(2)] double Latitude,
    [property: ProtoMember(3)] double Longitude,
    [property: ProtoMember(4)] DateTime Timestamp);