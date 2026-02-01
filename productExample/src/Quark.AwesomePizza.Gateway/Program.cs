using System.Text.Json;
using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Client;
using Quark.Extensions.DependencyInjection;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON options for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});
ConfigureServices(builder);

void ConfigureServices(WebApplicationBuilder webApplicationBuilder)
{
    var configuration = webApplicationBuilder.Configuration;
    var siloId = Environment.GetEnvironmentVariable("SILO_ID")
                 ?? configuration["Silo:Id"]
                 ?? $"silo-{Guid.NewGuid():N}";

    var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST")
                    ?? configuration["Redis:Host"]
                    ?? "localhost";
    // Add logging
    webApplicationBuilder.Services.AddLogging();
    webApplicationBuilder.Services.UseQuarkClient(
        configure: options => options.ClientId = siloId,
        clientBuilderConfigure: clientBuilderConfigure =>
            clientBuilderConfigure.WithRedisClustering(connectionString: redisHost)
                .WithGrpcTransport());
}

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


app.UseCors();

// Banner
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║  Awesome Pizza - Gateway API         ║");
Console.WriteLine("║  REST API connecting to Silo         ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("⚠️  NOTE: This gateway should connect to Silo");
Console.WriteLine("    For now, it creates local actors (demo mode)");
Console.WriteLine("    In production: Use IClusterClient or gRPC");
Console.WriteLine();

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
app.MapPost("/api/orders", async (CreateOrderRequest request, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();

    var orderId = $"order-{Guid.NewGuid():N}";
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var response = await actor.CreateOrderAsync(request);
    return Results.Created($"/api/orders/{orderId}", response);
});

/// <summary>
/// Get order details
/// </summary>
app.MapGet("/api/orders/{orderId}", async (string orderId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var order = await actor.GetOrderAsync();
    return order != null ? Results.Ok(order) : Results.NotFound();
});

/// <summary>
/// Confirm an order (send to kitchen)
/// </summary>
app.MapPost("/api/orders/{orderId}/confirm", async (string orderId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);


    var order = await actor.ConfirmOrderAsync();
    return Results.Ok(order);
});

/// <summary>
/// Assign a driver to an order
/// </summary>
app.MapPost("/api/orders/{orderId}/assign-driver", async (string orderId, string driverId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var order = await actor.AssignDriverAsync(driverId);
    return Results.Ok(order);
});

/// <summary>
/// Start delivery (driver picked up the order)
/// </summary>
app.MapPost("/api/orders/{orderId}/start-delivery", async (string orderId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);


    var order = await actor.StartDeliveryAsync();
    return Results.Ok(order);
});

/// <summary>
/// Complete delivery (mark as delivered)
/// </summary>
app.MapPost("/api/orders/{orderId}/complete-delivery", async (string orderId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var order = await actor.CompleteDeliveryAsync();
    return Results.Ok(order);
});

/// <summary>
/// Cancel an order
/// </summary>
app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, string reason, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var order = await actor.CancelOrderAsync(reason);
    return Results.Ok(order);
});

/// <summary>
/// Real-time order tracking using Server-Sent Events (SSE)
/// </summary>
app.MapGet("/api/orders/{orderId}/track", async (string orderId, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IOrderActor>(orderId);

    var response = ctx.Response;
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
        await Task.Delay(Timeout.Infinite, ctx.RequestAborted);
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
app.MapPost("/api/drivers", async (string driverId, string name) => { });

/// <summary>
/// Get driver status
/// </summary>
app.MapGet("/api/drivers/{driverId}", async (string driverId, HttpContext ctx) => { });

/// <summary>
/// Update driver location (typically called by MQTT bridge)
/// </summary>
app.MapPost("/api/drivers/{driverId}/location", async (
    string driverId,
    UpdateDriverLocationRequest request, HttpContext ctx) =>
{
});

/// <summary>
/// Update driver status
/// </summary>
app.MapPost("/api/drivers/{driverId}/status", async (string driverId, DriverStatus status, HttpContext ctx) =>
{
    var cluster = ctx.RequestServices.GetRequiredService<IClusterClient>();
    var actor = cluster.GetActor<IDriverActor>(driverId);
    var driver = await actor.UpdateStatusAsync(status);
    return Results.Ok(driver);
});

// ===== Health Check =====

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "Awesome Pizza Gateway",
    // activeActors = activeActors.Count
}));

// Start the application
Console.WriteLine($"Gateway API starting on: http://localhost:5000");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine();

app.Run();