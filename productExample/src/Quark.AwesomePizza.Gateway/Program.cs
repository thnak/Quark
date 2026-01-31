using System.Collections.Concurrent;
using System.Text.Json;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Shared.Actors;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// In-memory actor registry (simulates cluster client)
// In a real distributed system, this would use Quark.Client with Redis clustering
var actorFactory = new ActorFactory();
var activeActors = new ConcurrentDictionary<string, IActor>();

app.UseCors();

// Banner
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║  Awesome Pizza - Gateway API         ║");
Console.WriteLine("║  REST API + Real-time SSE            ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

// Helper methods
async Task<OrderActor> GetOrCreateOrderActorAsync(string orderId)
{
    if (!activeActors.TryGetValue(orderId, out var actorBase))
    {
        return null!;
    }
    return actorBase as OrderActor ?? null!;
}

async Task<DriverActor> GetOrCreateDriverActorAsync(string driverId)
{
    if (!activeActors.TryGetValue(driverId, out var actorBase))
    {
        var actor = actorFactory.CreateActor<DriverActor>(driverId);
        await actor.OnActivateAsync();
        activeActors[driverId] = actor;
        return actor;
    }
    return actorBase as DriverActor ?? null!;
}

// ===== Root Endpoint =====

app.MapGet("/", () => Results.Ok(new
{
    Service = "Awesome Pizza Gateway API",
    Version = "1.0.0",
    Description = "REST API for pizza ordering, tracking, and management",
    Endpoints = new[]
    {
        "POST /api/orders - Create new order",
        "GET /api/orders/{orderId} - Get order status",
        "POST /api/orders/{orderId}/confirm - Confirm order",
        "POST /api/orders/{orderId}/assign-driver - Assign driver",
        "POST /api/orders/{orderId}/start-delivery - Start delivery",
        "POST /api/orders/{orderId}/complete-delivery - Complete delivery",
        "POST /api/orders/{orderId}/cancel - Cancel order",
        "GET /api/orders/{orderId}/track - Real-time tracking (SSE)",
        "POST /api/drivers - Register driver",
        "GET /api/drivers/{driverId} - Get driver status",
        "POST /api/drivers/{driverId}/location - Update driver location",
        "GET /health - Health check"
    }
}));

// ===== Order Endpoints =====

/// <summary>
/// Create a new pizza order
/// </summary>
app.MapPost("/api/orders", async (CreateOrderRequest request) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";
    var actor = actorFactory.CreateActor<OrderActor>(orderId);
    
    await actor.OnActivateAsync();
    activeActors[orderId] = actor;
    
    var response = await actor.CreateOrderAsync(request);
    return Results.Created($"/api/orders/{orderId}", response);
});

/// <summary>
/// Get order details
/// </summary>
app.MapGet("/api/orders/{orderId}", async (string orderId) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.GetOrderAsync();
    return order != null ? Results.Ok(order) : Results.NotFound();
});

/// <summary>
/// Confirm an order (send to kitchen)
/// </summary>
app.MapPost("/api/orders/{orderId}/confirm", async (string orderId) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.ConfirmOrderAsync();
    return Results.Ok(order);
});

/// <summary>
/// Assign a driver to an order
/// </summary>
app.MapPost("/api/orders/{orderId}/assign-driver", async (string orderId, string driverId) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.AssignDriverAsync(driverId);
    return Results.Ok(order);
});

/// <summary>
/// Start delivery (driver picked up the order)
/// </summary>
app.MapPost("/api/orders/{orderId}/start-delivery", async (string orderId) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.StartDeliveryAsync();
    return Results.Ok(order);
});

/// <summary>
/// Complete delivery (mark as delivered)
/// </summary>
app.MapPost("/api/orders/{orderId}/complete-delivery", async (string orderId) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.CompleteDeliveryAsync();
    return Results.Ok(order);
});

/// <summary>
/// Cancel an order
/// </summary>
app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, string reason) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var order = await actor.CancelOrderAsync(reason);
    return Results.Ok(order);
});

/// <summary>
/// Real-time order tracking using Server-Sent Events (SSE)
/// </summary>
app.MapGet("/api/orders/{orderId}/track", async (string orderId, HttpContext context) =>
{
    var actor = await GetOrCreateOrderActorAsync(orderId);
    if (actor == null)
        return Results.NotFound(new { message = $"Order {orderId} not found" });
    
    var response = context.Response;
    response.Headers.Append("Content-Type", "text/event-stream");
    response.Headers.Append("Cache-Control", "no-cache");
    response.Headers.Append("Connection", "keep-alive");
    
    void OnUpdate(OrderStatusUpdate update)
    {
        var json = JsonSerializer.Serialize(update);
        response.WriteAsync($"data: {json}\n\n").GetAwaiter().GetResult();
        response.Body.FlushAsync().GetAwaiter().GetResult();
    }
    
    actor.Subscribe(OnUpdate);
    
    try
    {
        // Send initial state
        var currentState = await actor.GetOrderAsync();
        if (currentState != null)
        {
            var initialUpdate = new OrderStatusUpdate(
                orderId,
                currentState.Status,
                DateTime.UtcNow,
                currentState.CurrentDriverLocation,
                "Connected to order tracking");
            
            var json = JsonSerializer.Serialize(initialUpdate);
            await response.WriteAsync($"data: {json}\n\n");
            await response.Body.FlushAsync();
        }
        
        // Keep connection alive
        await Task.Delay(Timeout.Infinite, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected
    }
    finally
    {
        actor.Unsubscribe(OnUpdate);
    }
    
    return Results.Empty;
});

// ===== Driver Endpoints =====

/// <summary>
/// Register a new driver
/// </summary>
app.MapPost("/api/drivers", async (string driverId, string name) =>
{
    var actor = await GetOrCreateDriverActorAsync(driverId);
    var driver = await actor.InitializeAsync(name);
    return Results.Created($"/api/drivers/{driverId}", driver);
});

/// <summary>
/// Get driver status
/// </summary>
app.MapGet("/api/drivers/{driverId}", async (string driverId) =>
{
    var actor = await GetOrCreateDriverActorAsync(driverId);
    if (actor == null)
        return Results.NotFound(new { message = $"Driver {driverId} not found" });
    
    var driver = await actor.GetStateAsync();
    return driver != null ? Results.Ok(driver) : Results.NotFound();
});

/// <summary>
/// Update driver location (typically called by MQTT bridge)
/// </summary>
app.MapPost("/api/drivers/{driverId}/location", async (
    string driverId,
    UpdateDriverLocationRequest request) =>
{
    var actor = await GetOrCreateDriverActorAsync(driverId);
    var driver = await actor.UpdateLocationAsync(
        request.Latitude,
        request.Longitude,
        request.Timestamp);
    
    return Results.Ok(driver);
});

/// <summary>
/// Update driver status
/// </summary>
app.MapPost("/api/drivers/{driverId}/status", async (string driverId, DriverStatus status) =>
{
    var actor = await GetOrCreateDriverActorAsync(driverId);
    var driver = await actor.UpdateStatusAsync(status);
    return Results.Ok(driver);
});

// ===== Health Check =====

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "Awesome Pizza Gateway",
    activeActors = activeActors.Count
}));

// Start the application
Console.WriteLine($"Gateway API starting on: http://localhost:5000");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine();

app.Run();
