using FastEndpoints;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Shared.Actors;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Endpoint for creating a new pizza order.
/// </summary>
public class CreateOrderEndpoint : Endpoint<CreateOrderRequest, CreateOrderResponse>
{
    public override void Configure()
    {
        Post("/api/orders");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
    {
        var actorFactory = Resolve<IActorFactory>();
        
        var orderId = $"order-{Guid.NewGuid():N}";
        var pizzaActor = actorFactory.GetOrCreateActor<PizzaActor>(orderId);
        
        await pizzaActor.OnActivateAsync(ct);
        var order = await pizzaActor.CreateOrderAsync(req.CustomerId, req.PizzaType);

        await Send.OkAsync(new CreateOrderResponse(
            order.OrderId,
            order.Status,
            order.OrderTime), cancellation: ct);
    }
}
