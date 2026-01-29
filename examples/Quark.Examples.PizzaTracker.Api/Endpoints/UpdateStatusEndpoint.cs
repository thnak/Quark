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
    public override void Configure()
    {
        Put("/api/orders/{orderId}/status");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateStatusRequest req, CancellationToken ct)
    {
        var actorFactory = Resolve<IActorFactory>();
        
        var orderId = Route<string>("orderId")!;
        var pizzaActor = actorFactory.GetOrCreateActor<PizzaActor>(orderId);
        
        // If assigning a driver, also update the driver actor
        if (!string.IsNullOrEmpty(req.DriverId))
        {
            var driverActor = actorFactory.GetOrCreateActor<DeliveryDriverActor>(req.DriverId);
            await driverActor.AssignOrderAsync(orderId);
        }
        
        var order = await pizzaActor.UpdateStatusAsync(req.Status, req.DriverId);
        await SendAsync(order, cancellation: ct);
    }
}
