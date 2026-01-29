using System.Threading.Channels;
using FastEndpoints;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Shared.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Server-Sent Events endpoint for tracking pizza orders in real-time.
/// </summary>
public class TrackPizzaEndpoint : Endpoint<EmptyRequest>
{
    public override void Configure()
    {
        Get("/api/orders/{orderId}/track");
        AllowAnonymous();
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var actorFactory = Resolve<IActorFactory>();

        var orderId = Route<string>("orderId")!;

        // Set up SSE headers
        HttpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
        HttpContext.Response.Headers.Append("Connection", "keep-alive");

        var pizzaActor = actorFactory.GetOrCreateActor<PizzaActor>(orderId);

        // Send initial state
        await pizzaActor.GetOrderAsync();

        // Subscribe to updates
        var updateChannel = Channel.CreateUnbounded<PizzaStatusUpdate>();

        pizzaActor.Subscribe(update => { updateChannel.Writer.TryWrite(update); });

        // Stream updates to client
        try
        {
            await Send.EventStreamAsync("status", updateChannel.Reader.ReadAllAsync(ct), ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }
}