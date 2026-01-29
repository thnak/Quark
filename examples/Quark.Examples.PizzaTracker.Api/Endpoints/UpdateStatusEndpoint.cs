using FastEndpoints;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Shared.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Request to update pizza status.
/// </summary>
public record UpdateStatusRequest(PizzaStatus Status, string? DriverId);

/// <summary>
/// Endpoint for updating pizza order status.
/// </summary>
public class UpdateStatusEndpoint : Endpoint<UpdateStatusRequest>
{
    private readonly IActorFactory _actorFactory = null!;

    public override void Configure()
    {
        Put("/api/orders/{orderId}/status");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateStatusRequest req, CancellationToken ct)
    {
        var orderId = Route<string>("orderId")!;
        var pizzaActor = _actorFactory.GetOrCreateActor<PizzaActor>(orderId);
        
        var order = await pizzaActor.UpdateStatusAsync(req.Status, req.DriverId);
        await SendAsync(order, cancellation: ct);
    }
}
