using FastEndpoints;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Empty request for endpoints without body.
/// </summary>
public class EmptyRequest 
{ 
    public string? Dummy { get; set; }
}

/// <summary>
/// Response for the root endpoint.
/// </summary>
public record ApiInfoResponse(
    string Service,
    string Version,
    string Framework,
    string[] Endpoints);

/// <summary>
/// Root endpoint that returns API information.
/// </summary>
public class RootEndpoint : Endpoint<EmptyRequest, ApiInfoResponse>
{
    public override void Configure()
    {
        Get("/");
        AllowAnonymous();
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        await Send.OkAsync(new ApiInfoResponse(
            Service: "Pizza GPS Tracker API",
            Version: "1.0.0",
            Framework: "Quark - AOT Compatible Actor Framework",
            Endpoints: new[]
            {
                "POST /api/orders - Create a new pizza order",
                "PUT /api/orders/{orderId}/status - Update order status",
                "PUT /api/drivers/{driverId}/location - Update driver GPS location",
                "GET /api/orders/{orderId}/track - Subscribe to real-time order tracking (SSE)"
            }), cancellation: ct);
    }
}
