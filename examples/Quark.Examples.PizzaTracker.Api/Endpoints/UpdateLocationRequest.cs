namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Request to update driver GPS location.
/// </summary>
public record UpdateLocationRequest(double Latitude, double Longitude);