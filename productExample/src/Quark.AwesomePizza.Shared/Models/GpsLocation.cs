namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents a GPS location with timestamp.
/// </summary>
public record GpsLocation(
    double Latitude,
    double Longitude,
    DateTime Timestamp,
    double? Accuracy = null);