using System.Collections.Concurrent;
using System.Text.Json;
using FastEndpoints;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Shared.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api.Endpoints;

/// <summary>
/// Server-Sent Events endpoint for tracking pizza orders in real-time.
/// </summary>
public class TrackPizzaEndpoint : EndpointWithoutRequest
{
    private readonly IActorFactory _actorFactory = null!;
    private static readonly ConcurrentDictionary<string, List<Action<PizzaStatusUpdate>>> _subscribers = new();

    public override void Configure()
    {
        Get("/api/orders/{orderId}/track");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderId = Route<string>("orderId")!;
        
        // Set up SSE headers
        HttpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
        HttpContext.Response.Headers.Append("Connection", "keep-alive");

        var pizzaActor = _actorFactory.GetOrCreateActor<PizzaActor>(orderId);
        
        // Send initial state
        var currentOrder = await pizzaActor.GetOrderAsync();
        if (currentOrder != null)
        {
            var initialUpdate = new PizzaStatusUpdate(
                currentOrder.OrderId,
                currentOrder.Status,
                DateTime.UtcNow,
                currentOrder.DriverLocation);
            
            await SendSseEvent("status", initialUpdate, ct);
        }

        // Subscribe to updates
        var updateChannel = System.Threading.Channels.Channel.CreateUnbounded<PizzaStatusUpdate>();
        
        pizzaActor.Subscribe(update =>
        {
            updateChannel.Writer.TryWrite(update);
        });

        // Stream updates to client
        try
        {
            await foreach (var update in updateChannel.Reader.ReadAllAsync(ct))
            {
                await SendSseEvent("status", update, ct);
                await HttpContext.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }

    private async Task SendSseEvent(string eventType, PizzaStatusUpdate data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, PizzaTrackerJsonContext.Default.PizzaStatusUpdate);
        var sseData = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(sseData);
        await HttpContext.Response.Body.WriteAsync(bytes, ct);
    }
}
