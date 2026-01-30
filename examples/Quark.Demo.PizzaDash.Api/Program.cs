using System.Collections.Concurrent;
using System.Text.Json;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

// In-memory actor registry (simulates cluster client)
var actorFactory = new ActorFactory();
var activeActors = new ConcurrentDictionary<string, IActor>();

app.MapGet("/", () => Results.Ok(new
{
    Service = "Quark Pizza Dash API",
    Version = "1.0.0",
    Description = "Customer-facing REST API for pizza ordering",
    Endpoints = new[]
    {
        "POST /api/orders - Create new order",
        "GET /api/orders/{orderId} - Get order status",
        "PUT /api/orders/{orderId}/status - Update order status",
        "POST /api/orders/{orderId}/driver - Assign driver",
        "PUT /api/drivers/{driverId}/location - Update driver location",
        "GET /api/orders/{orderId}/track - Real-time tracking (SSE)"
    }
}));

// Create new order
app.MapPost("/api/orders", async (CreateOrderRequest request) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";
    var actor = actorFactory.CreateActor<OrderActor>(orderId);
    
    await actor.OnActivateAsync();
    activeActors[orderId] = actor;

    var order = await actor.CreateOrderAsync(request.CustomerId, request.PizzaType);

    return Results.Created($"/api/orders/{orderId}", order);
});

// Get order status
app.MapGet("/api/orders/{orderId}", async (string orderId) =>
{
    if (!activeActors.TryGetValue(orderId, out var actorBase) || actorBase is not OrderActor actor)
    {
        return Results.NotFound(new { Error = "Order not found" });
    }

    var order = await actor.GetOrderAsync();
    return order == null ? Results.NotFound() : Results.Ok(order);
});

// Update order status
app.MapPut("/api/orders/{orderId}/status", async (string orderId, UpdateStatusRequest request) =>
{
    if (!activeActors.TryGetValue(orderId, out var actorBase) || actorBase is not OrderActor actor)
    {
        return Results.NotFound(new { Error = "Order not found" });
    }

    var order = await actor.UpdateStatusAsync(request.NewStatus, request.DriverId);
    return Results.Ok(order);
});

// Assign driver to order
app.MapPost("/api/orders/{orderId}/driver", async (string orderId, string driverId) =>
{
    if (!activeActors.TryGetValue(orderId, out var actorBase) || actorBase is not OrderActor actor)
    {
        return Results.NotFound(new { Error = "Order not found" });
    }

    var order = await actor.AssignDriverAsync(driverId);
    return Results.Ok(order);
});

// Update driver location
app.MapPut("/api/drivers/{driverId}/location", async (string driverId, GpsLocation location) =>
{
    // Get or create driver actor
    if (!activeActors.TryGetValue(driverId, out var actorBase))
    {
        var driverActor = actorFactory.CreateActor<DeliveryDriverActor>(driverId);
        await driverActor.OnActivateAsync();
        activeActors[driverId] = driverActor;
        actorBase = driverActor;
    }

    if (actorBase is not DeliveryDriverActor driver)
    {
        return Results.BadRequest(new { Error = "Not a driver actor" });
    }

    var assignedOrderId = await driver.GetAssignedOrderAsync();
    if (assignedOrderId != null && activeActors.TryGetValue(assignedOrderId, out var orderActorBase) 
        && orderActorBase is OrderActor orderActor)
    {
        await orderActor.UpdateDriverLocationAsync(location);
    }

    var updatedLocation = await driver.UpdateLocationAsync(location.Latitude, location.Longitude);
    return Results.Ok(updatedLocation);
});

// Real-time tracking with Server-Sent Events (SSE)
app.MapGet("/api/orders/{orderId}/track", async (string orderId, HttpContext context) =>
{
    if (!activeActors.TryGetValue(orderId, out var actorBase) || actorBase is not OrderActor actor)
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");

    var updates = System.Threading.Channels.Channel.CreateUnbounded<OrderStatusUpdate>();

    void OnUpdate(OrderStatusUpdate update)
    {
        updates.Writer.TryWrite(update);
    }

    actor.Subscribe(OnUpdate);

    try
    {
        // Send current state first
        var currentOrder = await actor.GetOrderAsync();
        if (currentOrder != null)
        {
            var initialUpdate = new OrderStatusUpdate(
                currentOrder.OrderId,
                currentOrder.Status,
                currentOrder.LastUpdated,
                currentOrder.DriverLocation);
            
            var json = JsonSerializer.Serialize(initialUpdate);
            await context.Response.WriteAsync($"data: {json}\n\n");
            await context.Response.Body.FlushAsync();
        }

        // Stream updates
        await foreach (var update in updates.Reader.ReadAllAsync(context.RequestAborted))
        {
            var json = JsonSerializer.Serialize(update);
            await context.Response.WriteAsync($"data: {json}\n\n");
            await context.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected
    }
    finally
    {
        actor.Unsubscribe(OnUpdate);
    }
});

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    ActiveActors = activeActors.Count,
    Timestamp = DateTime.UtcNow
}));

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Quark Pizza Dash - Customer API                   ║");
Console.WriteLine("║       REST Gateway for Pizza Ordering                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

app.Run();
