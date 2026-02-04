using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Request to update pizza status.
/// </summary>
public record UpdateStatusRequest(PizzaStatus Status, string? DriverId);