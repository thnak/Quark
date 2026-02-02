using ProtoBuf;

namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents driver state.
/// </summary>
[ProtoContract]
public record DriverState
{
    [ProtoMember(1)]
    public string DriverId { get; set; } = "";
    
    [ProtoMember(2)]
    public string Name { get; set; } = "";
    
    [ProtoMember(3)]
    public DriverStatus Status { get; set; }
    
    [ProtoMember(4)]
    public GpsLocation? CurrentLocation { get; set; }
    
    [ProtoMember(5)]
    public string? CurrentOrderId { get; set; }
    
    [ProtoMember(6)]
    public DateTime LastUpdated { get; set; }
    
    [ProtoMember(7)]
    public int DeliveredToday { get; set; }
}