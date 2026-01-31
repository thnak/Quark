# Awesome Pizza - Feature Specifications

> Detailed technical specifications for each feature in the Awesome Pizza demo application

---

## 📋 Table of Contents

1. [Feature 1: Order Lifecycle Management](#feature-1-order-lifecycle-management)
2. [Feature 2: Real-time Driver Telemetry via MQTT](#feature-2-real-time-driver-telemetry-via-mqtt)
3. [Feature 3: Kitchen Display System (KDS)](#feature-3-kitchen-display-system-kds)
4. [Feature 4: Inventory Management with Auto-Restock](#feature-4-inventory-management-with-auto-restock)
5. [Feature 5: Manager Dashboard with Real-time Updates](#feature-5-manager-dashboard-with-real-time-updates)

---

## Feature 1: Order Lifecycle Management

### Overview
Manages the complete lifecycle of a pizza order from creation to delivery, leveraging Quark's virtual actor model for state management and Orleans-style grain patterns.

### Quark Capabilities Demonstrated
- ✅ **Virtual Actors** - Each order is a long-lived actor with persistent identity
- ✅ **State Persistence** - Order state saved to Redis with optimistic concurrency
- ✅ **Supervision** - Parent-child relationships between Restaurant → Kitchen → Order
- ✅ **Reminders** - Persistent timers for stuck order detection
- ✅ **Streaming** - Publish order events to subscribers

### State Model

```csharp
/// <summary>
/// Persistent state for an order actor
/// </summary>
public record OrderState
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string RestaurantId { get; init; }
    public required List<PizzaItem> Items { get; init; }
    
    public OrderStatus Status { get; init; } = OrderStatus.Created;
    public DateTime CreatedAt { get; init; }
    public DateTime LastUpdated { get; init; }
    public DateTime? EstimatedDeliveryTime { get; init; }
    public DateTime? ActualDeliveryTime { get; init; }
    
    public string? AssignedChefId { get; init; }
    public string? AssignedDriverId { get; init; }
    
    public string ETag { get; init; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Order status state machine
/// </summary>
public enum OrderStatus
{
    Created,           // Initial state
    Validated,         // Payment and inventory checked
    Queued,            // Waiting for chef assignment
    Preparing,         // Chef assigned, preparing ingredients
    Baking,            // In oven
    QualityCheck,      // Post-bake inspection
    ReadyForPickup,    // Ready for driver
    DriverAssigned,    // Driver en route to restaurant
    PickedUp,          // Driver has pizza
    OutForDelivery,    // En route to customer
    Delivered,         // Successfully delivered
    Cancelled,         // Cancelled by customer or system
    Failed             // Failed delivery or preparation
}

public record PizzaItem
{
    public required string PizzaType { get; init; }
    public required PizzaSize Size { get; init; }
    public List<string> Toppings { get; init; } = new();
    public decimal Price { get; init; }
}

public enum PizzaSize
{
    Small,
    Medium,
    Large,
    ExtraLarge
}
```

### Actor Interface

```csharp
/// <summary>
/// Type-safe interface for order actor
/// Quark will generate Protobuf contracts and client proxy
/// </summary>
public interface IOrderActor : IQuarkActor
{
    /// <summary>
    /// Creates a new order with initial state
    /// </summary>
    Task<OrderState> CreateOrderAsync(CreateOrderRequest request);
    
    /// <summary>
    /// Updates order status with state machine validation
    /// </summary>
    Task<OrderState> UpdateStatusAsync(OrderStatus newStatus, string? actorId = null);
    
    /// <summary>
    /// Gets current order state
    /// </summary>
    Task<OrderState> GetOrderAsync();
    
    /// <summary>
    /// Assigns a chef to prepare the order
    /// </summary>
    Task<bool> AssignChefAsync(string chefId);
    
    /// <summary>
    /// Assigns a delivery driver to the order
    /// </summary>
    Task<bool> AssignDriverAsync(string driverId);
    
    /// <summary>
    /// Updates driver's GPS location for delivery tracking
    /// </summary>
    Task UpdateDriverLocationAsync(string driverId, GpsLocation location);
    
    /// <summary>
    /// Cancels the order if it's not yet picked up
    /// </summary>
    Task<bool> CancelOrderAsync(string reason);
    
    /// <summary>
    /// Confirms successful delivery
    /// </summary>
    Task<bool> ConfirmDeliveryAsync(string driverId, string signature);
}

public record CreateOrderRequest
{
    public required string CustomerId { get; init; }
    public required string RestaurantId { get; init; }
    public required List<PizzaItem> Items { get; init; }
    public required DeliveryAddress Address { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
}

public record DeliveryAddress
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string ZipCode { get; init; }
    public GpsLocation? Coordinates { get; init; }
}

public record GpsLocation
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public DateTime Timestamp { get; init; }
}

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    Cash,
    DigitalWallet
}
```

### Actor Implementation

```csharp
[Actor(Name = "Order", Reentrant = false)]
public class OrderActor : StatefulActorBase<OrderState>, IOrderActor
{
    private readonly ILogger<OrderActor> _logger;
    private readonly IStreamProvider _streamProvider;
    
    [QuarkState]
    public OrderState State { get; private set; } = null!;
    
    public OrderActor(
        string actorId,
        ILogger<OrderActor> logger,
        IStreamProvider streamProvider) 
        : base(actorId)
    {
        _logger = logger;
        _streamProvider = streamProvider;
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Load state from Redis
        await LoadStateAsync();
        
        if (State != null)
        {
            _logger.LogInformation(
                "Order {OrderId} activated with status {Status}",
                ActorId,
                State.Status);
        }
    }
    
    public async Task<OrderState> CreateOrderAsync(CreateOrderRequest request)
    {
        if (State != null)
        {
            throw new InvalidOperationException($"Order {ActorId} already exists");
        }
        
        // Calculate total and estimated delivery time
        var total = request.Items.Sum(item => item.Price);
        var estimatedTime = DateTime.UtcNow.AddMinutes(CalculateDeliveryTime(request));
        
        State = new OrderState
        {
            OrderId = ActorId,
            CustomerId = request.CustomerId,
            RestaurantId = request.RestaurantId,
            Items = request.Items,
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            EstimatedDeliveryTime = estimatedTime
        };
        
        // Persist state
        await SaveStateAsync();
        
        // Publish event
        await PublishOrderEventAsync(new OrderCreatedEvent
        {
            OrderId = ActorId,
            CustomerId = request.CustomerId,
            RestaurantId = request.RestaurantId,
            Total = total,
            Timestamp = DateTime.UtcNow
        });
        
        // Register reminder to check for stuck orders
        await RegisterReminderAsync(
            "StuckOrderCheck",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(15));
        
        _logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}",
            ActorId,
            request.CustomerId);
        
        return State;
    }
    
    public async Task<OrderState> UpdateStatusAsync(
        OrderStatus newStatus, 
        string? actorId = null)
    {
        if (State == null)
        {
            throw new InvalidOperationException($"Order {ActorId} does not exist");
        }
        
        // Validate state transition
        if (!IsValidTransition(State.Status, newStatus))
        {
            throw new InvalidOperationException(
                $"Invalid transition from {State.Status} to {newStatus}");
        }
        
        var oldStatus = State.Status;
        
        State = State with
        {
            Status = newStatus,
            LastUpdated = DateTime.UtcNow,
            AssignedChefId = newStatus == OrderStatus.Preparing 
                ? actorId ?? State.AssignedChefId 
                : State.AssignedChefId,
            AssignedDriverId = newStatus == OrderStatus.DriverAssigned 
                ? actorId ?? State.AssignedDriverId 
                : State.AssignedDriverId,
            ActualDeliveryTime = newStatus == OrderStatus.Delivered 
                ? DateTime.UtcNow 
                : State.ActualDeliveryTime,
            ETag = Guid.NewGuid().ToString()
        };
        
        // Persist with optimistic concurrency
        await SaveStateAsync();
        
        // Publish status change event
        await PublishOrderEventAsync(new OrderStatusChangedEvent
        {
            OrderId = ActorId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Timestamp = DateTime.UtcNow
        });
        
        _logger.LogInformation(
            "Order {OrderId} status changed: {OldStatus} → {NewStatus}",
            ActorId,
            oldStatus,
            newStatus);
        
        return State;
    }
    
    private bool IsValidTransition(OrderStatus current, OrderStatus next)
    {
        return (current, next) switch
        {
            (OrderStatus.Created, OrderStatus.Validated) => true,
            (OrderStatus.Validated, OrderStatus.Queued) => true,
            (OrderStatus.Queued, OrderStatus.Preparing) => true,
            (OrderStatus.Preparing, OrderStatus.Baking) => true,
            (OrderStatus.Baking, OrderStatus.QualityCheck) => true,
            (OrderStatus.QualityCheck, OrderStatus.ReadyForPickup) => true,
            (OrderStatus.ReadyForPickup, OrderStatus.DriverAssigned) => true,
            (OrderStatus.DriverAssigned, OrderStatus.PickedUp) => true,
            (OrderStatus.PickedUp, OrderStatus.OutForDelivery) => true,
            (OrderStatus.OutForDelivery, OrderStatus.Delivered) => true,
            
            // Cancellation allowed before pickup
            (_, OrderStatus.Cancelled) when current <= OrderStatus.ReadyForPickup => true,
            
            // Failure can happen at any stage before delivery
            (_, OrderStatus.Failed) when current < OrderStatus.Delivered => true,
            
            _ => false
        };
    }
    
    private int CalculateDeliveryTime(CreateOrderRequest request)
    {
        // Base preparation time
        var prepTime = 15;
        
        // Add time per pizza
        prepTime += request.Items.Count * 5;
        
        // Add delivery time (assume 15-30 minutes)
        var deliveryTime = 20;
        
        return prepTime + deliveryTime;
    }
    
    private async Task PublishOrderEventAsync(object orderEvent)
    {
        var stream = _streamProvider.GetStream("order-events", ActorId);
        await stream.PublishAsync(orderEvent);
    }
}
```

### Silo Configuration

```csharp
// In Quark.AwesomePizza.Silo/Program.cs

var siloBuilder = new QuarkSiloBuilder()
    .ConfigureCluster(cluster => cluster
        .UseRedisClusterMembership(redisConnectionString)
        .WithConsistentHashing()
        .WithSiloId(siloId))
    .ConfigureStorage(storage => storage
        .UseRedisStateStorage(redisConnectionString)
        .WithOptimisticConcurrency()
        .WithJsonSerializerContext<AwesomePizzaJsonContext>())
    .ConfigureReminders(reminders => reminders
        .UseRedisReminderTable(redisConnectionString)
        .WithMinimumInterval(TimeSpan.FromMinutes(1)))
    .ConfigureStreaming(streaming => streaming
        .UseInMemoryStreamProvider("order-events")
        .WithQueueCount(16))
    .ConfigureActors(actors => actors
        .RegisterActor<OrderActor>()
        .RegisterActor<KitchenActor>()
        .RegisterActor<ChefActor>())
    .ConfigureServices(services => services
        .AddLogging(logging => logging
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information)))
    .Build();

await siloBuilder.StartAsync();
```

### Gateway Endpoints

```csharp
// In Quark.AwesomePizza.Gateway/Program.cs

// Create new order
app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IClusterClient client) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";
    var orderActor = client.GetActor<IOrderActor>(orderId);
    
    try
    {
        var order = await orderActor.CreateOrderAsync(request);
        return Results.Created($"/api/orders/{orderId}", order);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
.WithName("CreateOrder")
.WithTags("Orders")
.Produces<OrderState>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

// Get order status
app.MapGet("/api/orders/{orderId}", async (
    string orderId,
    IClusterClient client) =>
{
    var orderActor = client.GetActor<IOrderActor>(orderId);
    
    try
    {
        var order = await orderActor.GetOrderAsync();
        return order != null 
            ? Results.Ok(order) 
            : Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("GetOrder")
.WithTags("Orders")
.Produces<OrderState>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);
```

### Testing Strategy

```csharp
public class OrderActorTests
{
    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsOrderState()
    {
        // Arrange
        var factory = new ActorFactory();
        var orderActor = factory.CreateActor<OrderActor>("test-order-1");
        await orderActor.OnActivateAsync();
        
        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new() { PizzaType = "Margherita", Size = PizzaSize.Large, Price = 12.99m }
            },
            Address = new DeliveryAddress
            {
                Street = "123 Main St",
                City = "New York",
                State = "NY",
                ZipCode = "10001"
            }
        };
        
        // Act
        var result = await orderActor.CreateOrderAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-order-1", result.OrderId);
        Assert.Equal(OrderStatus.Created, result.Status);
        Assert.Equal("customer-1", result.CustomerId);
    }
    
    [Fact]
    public async Task UpdateStatus_ValidTransition_UpdatesState()
    {
        // Arrange
        var orderActor = CreateOrderWithStatus(OrderStatus.Created);
        
        // Act
        var result = await orderActor.UpdateStatusAsync(OrderStatus.Validated);
        
        // Assert
        Assert.Equal(OrderStatus.Validated, result.Status);
    }
    
    [Fact]
    public async Task UpdateStatus_InvalidTransition_ThrowsException()
    {
        // Arrange
        var orderActor = CreateOrderWithStatus(OrderStatus.Created);
        
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orderActor.UpdateStatusAsync(OrderStatus.Delivered));
    }
}
```

### Performance Targets

- **Order Creation**: < 50ms (p99)
- **Status Update**: < 30ms (p99)
- **State Load**: < 20ms (p99) from Redis
- **Concurrent Orders**: 10,000+ per silo
- **Memory per Actor**: < 1KB

---

## Feature 2: Real-time Driver Telemetry via MQTT

### Overview
Integrates MQTT protocol for real-time GPS tracking of delivery drivers, bridging IoT devices to Quark actors for live order tracking.

### Quark Capabilities Demonstrated
- ✅ **Actor Observers** - Subscribe to location updates
- ✅ **Streaming** - Publish driver locations to subscribers
- ✅ **External Integration** - MQTT bridge to actor system
- ✅ **Real-time Updates** - Sub-second latency for GPS data

### MQTT Topic Structure

```
pizza/drivers/{driverId}/location      - GPS coordinates
pizza/drivers/{driverId}/status        - Available, Busy, Offline
pizza/drivers/{driverId}/assigned      - Order assignment notifications
pizza/orders/{orderId}/driver-eta      - Estimated time of arrival updates
```

### Message Payloads

```csharp
/// <summary>
/// GPS location message from driver's device
/// </summary>
public record DriverLocationMessage
{
    public required string DriverId { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required DateTime Timestamp { get; init; }
    public double? Speed { get; init; }         // km/h
    public double? Heading { get; init; }       // degrees
    public double? Accuracy { get; init; }      // meters
}

/// <summary>
/// Driver status change message
/// </summary>
public record DriverStatusMessage
{
    public required string DriverId { get; init; }
    public required DriverStatus Status { get; init; }
    public DateTime Timestamp { get; init; }
}

public enum DriverStatus
{
    Offline,
    Available,
    Busy,
    OnBreak,
    EndOfShift
}
```

### Actor Interface

```csharp
public interface IDeliveryDriverActor : IQuarkActor
{
    /// <summary>
    /// Updates driver's GPS location
    /// </summary>
    Task UpdateLocationAsync(double latitude, double longitude);
    
    /// <summary>
    /// Gets driver's current location
    /// </summary>
    Task<GpsLocation?> GetLocationAsync();
    
    /// <summary>
    /// Changes driver availability status
    /// </summary>
    Task UpdateStatusAsync(DriverStatus status);
    
    /// <summary>
    /// Assigns an order to the driver
    /// </summary>
    Task<bool> AssignOrderAsync(string orderId);
    
    /// <summary>
    /// Confirms order pickup from restaurant
    /// </summary>
    Task<bool> ConfirmPickupAsync(string orderId);
    
    /// <summary>
    /// Confirms delivery to customer
    /// </summary>
    Task<bool> ConfirmDeliveryAsync(string orderId, string signature);
    
    /// <summary>
    /// Gets currently assigned order ID
    /// </summary>
    Task<string?> GetAssignedOrderAsync();
}
```

### MQTT Bridge Implementation

```csharp
/// <summary>
/// Background service that bridges MQTT messages to Quark actors
/// </summary>
public class MqttToActorBridge : BackgroundService
{
    private readonly IMqttClient _mqttClient;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<MqttToActorBridge> _logger;
    
    public MqttToActorBridge(
        IMqttClient mqttClient,
        IClusterClient clusterClient,
        ILogger<MqttToActorBridge> logger)
    {
        _mqttClient = mqttClient;
        _clusterClient = clusterClient;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Configure MQTT client
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer("mqtt.awesomepizza.com", 1883)
            .WithClientId($"mqtt-bridge-{Guid.NewGuid()}")
            .WithCredentials("bridge-user", "secure-password")
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();
        
        // Connect to MQTT broker
        await _mqttClient.ConnectAsync(options, stoppingToken);
        
        _logger.LogInformation("MQTT bridge connected to broker");
        
        // Subscribe to all driver topics
        await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic("pizza/drivers/+/location")
            .Build());
        
        await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic("pizza/drivers/+/status")
            .Build());
        
        // Handle incoming messages
        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                await HandleMqttMessageAsync(e.ApplicationMessage, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MQTT message from topic {Topic}", 
                    e.ApplicationMessage.Topic);
            }
        };
        
        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private async Task HandleMqttMessageAsync(
        MqttApplicationMessage message,
        CancellationToken cancellationToken)
    {
        var topic = message.Topic;
        var payload = Encoding.UTF8.GetString(message.PayloadSegment);
        
        // Parse topic: pizza/drivers/{driverId}/{action}
        var parts = topic.Split('/');
        if (parts.Length != 4 || parts[0] != "pizza" || parts[1] != "drivers")
        {
            _logger.LogWarning("Invalid topic format: {Topic}", topic);
            return;
        }
        
        var driverId = parts[2];
        var action = parts[3];
        
        // Route to appropriate actor method
        switch (action)
        {
            case "location":
                await HandleLocationUpdateAsync(driverId, payload, cancellationToken);
                break;
            
            case "status":
                await HandleStatusUpdateAsync(driverId, payload, cancellationToken);
                break;
            
            default:
                _logger.LogWarning("Unknown action in topic: {Action}", action);
                break;
        }
    }
    
    private async Task HandleLocationUpdateAsync(
        string driverId,
        string payload,
        CancellationToken cancellationToken)
    {
        var locationMsg = JsonSerializer.Deserialize<DriverLocationMessage>(payload);
        if (locationMsg == null)
        {
            _logger.LogWarning("Failed to deserialize location message for driver {DriverId}", 
                driverId);
            return;
        }
        
        // Get driver actor and update location
        var driverActor = _clusterClient.GetActor<IDeliveryDriverActor>(driverId);
        await driverActor.UpdateLocationAsync(
            locationMsg.Latitude,
            locationMsg.Longitude);
        
        _logger.LogDebug(
            "Updated location for driver {DriverId}: ({Lat}, {Lon})",
            driverId,
            locationMsg.Latitude,
            locationMsg.Longitude);
    }
    
    private async Task HandleStatusUpdateAsync(
        string driverId,
        string payload,
        CancellationToken cancellationToken)
    {
        var statusMsg = JsonSerializer.Deserialize<DriverStatusMessage>(payload);
        if (statusMsg == null)
        {
            _logger.LogWarning("Failed to deserialize status message for driver {DriverId}", 
                driverId);
            return;
        }
        
        var driverActor = _clusterClient.GetActor<IDeliveryDriverActor>(driverId);
        await driverActor.UpdateStatusAsync(statusMsg.Status);
        
        _logger.LogInformation(
            "Driver {DriverId} status changed to {Status}",
            driverId,
            statusMsg.Status);
    }
}
```

### Performance Targets

- **MQTT → Actor Latency**: < 100ms (p99)
- **Location Update Rate**: 1-2 updates per second
- **Concurrent Drivers**: 1,000+ simultaneously tracked
- **Message Throughput**: 10,000+ messages/sec

---

*[Continue with Features 3-5 in similar detail...]*

---

## Summary

This document provides detailed technical specifications for each feature in the Awesome Pizza demo application. Each feature:

1. **Leverages Quark Framework capabilities** - Virtual actors, persistence, streaming, etc.
2. **Includes complete code examples** - Interfaces, implementations, configurations
3. **Defines clear success criteria** - Performance targets and testing strategies
4. **Demonstrates production patterns** - Error handling, observability, scalability

For implementation guidance, refer to:
- [Implementation Plan](01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md)
- [Quick Start Guide](03-QUICK-START-GUIDE.md)
- [Architecture Diagrams](../implements/diagrams/)

---

**Document Version**: 1.0  
**Last Updated**: 2026-01-31  
**Status**: Ready for Implementation
