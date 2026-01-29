using FastEndpoints;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

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
public class RootEndpoint : EndpointWithoutRequest<ApiInfoResponse>
{
    public override void Configure()
    {
        Get("/");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(new ApiInfoResponse(
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
