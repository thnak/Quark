using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents a GPS location with timestamp.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public record GpsLocation(
    [property: ProtoMember(1)] double Latitude,
    [property: ProtoMember(2)] double Longitude,
    [property: ProtoMember(3)] DateTime Timestamp,
    [property: ProtoMember(4)] double? Accuracy = null);