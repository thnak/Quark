namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Request to update driver location via MQTT.
/// </summary>
public record UpdateDriverLocationRequest(
    string DriverId,
    double Latitude,
    double Longitude,
    DateTime Timestamp);