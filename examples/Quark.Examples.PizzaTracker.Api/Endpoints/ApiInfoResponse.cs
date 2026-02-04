namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Response for the root endpoint.
/// </summary>
public record ApiInfoResponse(
    string Service,
    string Version,
    string Framework,
    string[] Endpoints);