using FastEndpoints;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Shared.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Request to update driver GPS location.
/// </summary>
public record UpdateLocationRequest(double Latitude, double Longitude);

/// <summary>
/// Endpoint for updating delivery driver GPS location.
/// </summary>
public class UpdateDriverLocationEndpoint : Endpoint<UpdateLocationRequest>
{
    public override void Configure()
    {
        Put("/api/drivers/{driverId}/location");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateLocationRequest req, CancellationToken ct)
    {
        var actorFactory = Resolve<IActorFactory>();
        
        var driverId = Route<string>("driverId")!;
        var driverActor = actorFactory.GetOrCreateActor<DeliveryDriverActor>(driverId);
        
        await driverActor.UpdateLocationAsync(req.Latitude, req.Longitude);
        
        // Update pizza with driver location
        var currentOrderId = await driverActor.GetCurrentOrderIdAsync();
        if (!string.IsNullOrEmpty(currentOrderId))
        {
            var pizzaActor = actorFactory.GetOrCreateActor<PizzaActor>(currentOrderId);
            var location = await driverActor.GetLocationAsync();
            if (location != null)
            {
                await pizzaActor.UpdateDriverLocationAsync(location);
            }
        }

        await SendOkAsync(ct);
    }
}
