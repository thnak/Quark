using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

/// <summary>
/// Binary converter for GpsLocation model.
/// Provides AOT-compatible serialization for GPS location data.
/// </summary>
public class GpsLocationConverter : QuarkBinaryConverter<GpsLocation>
{
    public override void Write(BinaryWriter writer, GpsLocation value)
    {
        writer.Write(value.Latitude);
        writer.Write(value.Longitude);
        writer.Write(value.Timestamp.ToBinary());
        writer.Write(value.Accuracy.HasValue);
        if (value.Accuracy.HasValue)
        {
            writer.Write(value.Accuracy.Value);
        }
    }

    public override GpsLocation Read(BinaryReader reader)
    {
        var latitude = reader.ReadDouble();
        var longitude = reader.ReadDouble();
        var timestamp = DateTime.FromBinary(reader.ReadInt64());
        var hasAccuracy = reader.ReadBoolean();
        double? accuracy = hasAccuracy ? reader.ReadDouble() : null;

        return new GpsLocation(latitude, longitude, timestamp, accuracy);
    }
}
