using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

/// <summary>
/// Binary converter for DriverState model.
/// Provides AOT-compatible serialization for driver state data.
/// </summary>
public class DriverStateConverter : QuarkBinaryConverter<DriverState>
{
    private static readonly GpsLocationConverter _locationConverter = new();

    public override void Write(BinaryWriter writer, DriverState value)
    {
        writer.Write(value.DriverId);
        writer.Write(value.Name);
        writer.Write((int)value.Status);
        
        writer.Write(value.CurrentLocation != null);
        if (value.CurrentLocation != null)
        {
            _locationConverter.Write(writer, value.CurrentLocation);
        }
        
        writer.Write(value.CurrentOrderId ?? string.Empty);
        writer.Write(value.LastUpdated.ToBinary());
        writer.Write(value.DeliveredToday);
    }

    public override DriverState Read(BinaryReader reader)
    {
        var driverId = reader.ReadString();
        var name = reader.ReadString();
        var status = (DriverStatus)reader.ReadInt32();
        
        var hasLocation = reader.ReadBoolean();
        GpsLocation? currentLocation = hasLocation ? _locationConverter.Read(reader) : null;
        
        var currentOrderId = reader.ReadString();
        if (string.IsNullOrEmpty(currentOrderId))
            currentOrderId = null;
            
        var lastUpdated = DateTime.FromBinary(reader.ReadInt64());
        var deliveredToday = reader.ReadInt32();

        return new DriverState
        {
            DriverId = driverId,
            Name = name,
            Status = status,
            CurrentLocation = currentLocation,
            CurrentOrderId = currentOrderId,
            LastUpdated = lastUpdated,
            DeliveredToday = deliveredToday
        };
    }
}
