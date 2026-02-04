namespace Quark.AwesomePizza.MqttBridge;

/// <summary>
/// Message payload for driver location updates.
/// Supports multiple field name formats (lat/latitude, lon/longitude).
/// </summary>
internal class DriverLocationMessage
{
    public double? Lat { get; set; }
    public double? Latitude { get; set; }
    public double? Lon { get; set; }
    public double? Longitude { get; set; }
    public DateTime? Timestamp { get; set; }
}