# Awesome Pizza - Quick Start Guide

> Get your Awesome Pizza demo up and running in 15 minutes

---

## 🚀 Prerequisites

Before you begin, ensure you have the following installed:

### Required
- **.NET 10 SDK** (version 10.0.102 or later)
  ```bash
  dotnet --version
  # Should output: 10.0.102 or higher
  ```

- **Docker Desktop** (for Redis and MQTT broker)
  ```bash
  docker --version
  # Should output: Docker version 20.x or higher
  ```

### Optional (for development)
- **Visual Studio 2022** (v17.12+) or **VS Code**
- **Git** for version control
- **Postman** or **curl** for API testing

---

## 📁 Project Structure

The Awesome Pizza demo follows this structure:

```
productExample/
├── plans/                              # Planning documents
│   ├── 01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md
│   ├── 02-FEATURE-SPECIFICATIONS.md
│   └── 03-QUICK-START-GUIDE.md (this file)
│
├── src/                                # Source code (to be created)
│   ├── Quark.AwesomePizza.Shared/      # Shared interfaces and models
│   ├── Quark.AwesomePizza.Silo/        # AOT console app for actor hosting
│   ├── Quark.AwesomePizza.Gateway/     # Minimal API gateway
│   ├── Quark.AwesomePizza.MqttBridge/  # MQTT integration service
│   └── Quark.AwesomePizza.Tests/       # xUnit tests
│
└── implements/                         # Implementation tracking
    ├── tasks/                          # Task breakdown
    └── diagrams/                       # Architecture diagrams
```

---

## ⚡ Quick Setup (Development Mode)

### Step 1: Start Infrastructure Services

We'll use Docker Compose to start Redis and MQTT broker:

```bash
# Navigate to productExample directory
cd /home/runner/work/Quark/Quark/productExample

# Create docker-compose.yml (see below)
# Then start services
docker-compose up -d

# Verify services are running
docker-compose ps
```

**docker-compose.yml**:
```yaml
version: '3.8'

services:
  redis:
    image: redis:7-alpine
    container_name: awesomepizza-redis
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    command: redis-server --appendonly yes
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5

  mosquitto:
    image: eclipse-mosquitto:2
    container_name: awesomepizza-mqtt
    ports:
      - "1883:1883"  # MQTT
      - "9001:9001"  # WebSocket
    volumes:
      - ./mosquitto.conf:/mosquitto/config/mosquitto.conf
      - mosquitto-data:/mosquitto/data
      - mosquitto-logs:/mosquitto/log
    healthcheck:
      test: ["CMD", "mosquitto_sub", "-t", "$$SYS/#", "-C", "1"]
      interval: 10s
      timeout: 5s
      retries: 3

volumes:
  redis-data:
  mosquitto-data:
  mosquitto-logs:
```

**mosquitto.conf**:
```
listener 1883
allow_anonymous true
persistence true
persistence_location /mosquitto/data/
log_dest file /mosquitto/log/mosquitto.log
log_dest stdout
```

### Step 2: Create the Solution Structure

```bash
# Navigate to productExample/src
cd /home/runner/work/Quark/Quark/productExample/src

# Create shared library
dotnet new classlib -n Quark.AwesomePizza.Shared -f net10.0

# Create silo console app
dotnet new console -n Quark.AwesomePizza.Silo -f net10.0

# Create gateway API
dotnet new web -n Quark.AwesomePizza.Gateway -f net10.0

# Create MQTT bridge service
dotnet new worker -n Quark.AwesomePizza.MqttBridge -f net10.0

# Create test project
dotnet new xunit -n Quark.AwesomePizza.Tests -f net10.0

# Create solution file
cd ..
dotnet new sln -n Quark.AwesomePizza

# Add projects to solution
dotnet sln add src/Quark.AwesomePizza.Shared/Quark.AwesomePizza.Shared.csproj
dotnet sln add src/Quark.AwesomePizza.Silo/Quark.AwesomePizza.Silo.csproj
dotnet sln add src/Quark.AwesomePizza.Gateway/Quark.AwesomePizza.Gateway.csproj
dotnet sln add src/Quark.AwesomePizza.MqttBridge/Quark.AwesomePizza.MqttBridge.csproj
dotnet sln add src/Quark.AwesomePizza.Tests/Quark.AwesomePizza.Tests.csproj
```

### Step 3: Add Quark Framework References

```bash
# Add references to Quark.Core (from parent repo)
cd src/Quark.AwesomePizza.Shared
dotnet add reference ../../../src/Quark.Core/Quark.Core.csproj

# CRITICAL: Add source generator reference
dotnet add reference ../../../src/Quark.Generators/Quark.Generators.csproj

# Edit .csproj to configure generator correctly
```

**Quark.AwesomePizza.Shared/Quark.AwesomePizza.Shared.csproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference Quark.Core for actor framework -->
    <ProjectReference Include="../../../src/Quark.Core/Quark.Core.csproj" />
    
    <!-- REQUIRED: Explicit source generator reference (not transitive) -->
    <ProjectReference Include="../../../src/Quark.Generators/Quark.Generators.csproj" 
                      OutputItemType="Analyzer" 
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

### Step 4: Build the Solution

```bash
cd /home/runner/work/Quark/Quark/productExample

# Restore dependencies
dotnet restore

# Build all projects (with parallel build)
dotnet build -maxcpucount

# Expected output: All 5 projects should build successfully
```

---

## 🎯 Phase 1: Hello World Order

Let's create a minimal working example - creating a pizza order.

### Step 1: Define the Order Model

**src/Quark.AwesomePizza.Shared/Models/OrderModels.cs**:
```csharp
namespace Quark.AwesomePizza.Shared.Models;

public record OrderState
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string PizzaType { get; init; }
    public OrderStatus Status { get; init; } = OrderStatus.Created;
    public DateTime CreatedAt { get; init; }
    public DateTime LastUpdated { get; init; }
}

public enum OrderStatus
{
    Created,
    Preparing,
    Baking,
    Ready,
    Delivered
}
```

### Step 2: Define the Actor Interface

**src/Quark.AwesomePizza.Shared/Actors/IOrderActor.cs**:
```csharp
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Actors;

/// <summary>
/// Type-safe interface for order actor.
/// Quark will generate Protobuf contracts and client proxy.
/// </summary>
public interface IOrderActor : IQuarkActor
{
    Task<OrderState> CreateOrderAsync(string customerId, string pizzaType);
    Task<OrderState> UpdateStatusAsync(OrderStatus newStatus);
    Task<OrderState?> GetOrderAsync();
}
```

### Step 3: Implement the Actor

**src/Quark.AwesomePizza.Shared/Actors/OrderActor.cs**:
```csharp
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Actors;

[Actor(Name = "Order", Reentrant = false)]
public class OrderActor : ActorBase, IOrderActor
{
    private OrderState? _state;

    public OrderActor(string actorId) : base(actorId)
    {
    }

    public Task<OrderState> CreateOrderAsync(string customerId, string pizzaType)
    {
        if (_state != null)
            throw new InvalidOperationException($"Order {ActorId} already exists");

        _state = new OrderState
        {
            OrderId = ActorId,
            CustomerId = customerId,
            PizzaType = pizzaType,
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        Console.WriteLine($"✅ Order {ActorId} created: {pizzaType} for {customerId}");
        
        return Task.FromResult(_state);
    }

    public Task<OrderState> UpdateStatusAsync(OrderStatus newStatus)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        _state = _state with
        {
            Status = newStatus,
            LastUpdated = DateTime.UtcNow
        };

        Console.WriteLine($"✅ Order {ActorId} status: {newStatus}");

        return Task.FromResult(_state);
    }

    public Task<OrderState?> GetOrderAsync()
    {
        return Task.FromResult(_state);
    }
}
```

### Step 4: Create the Silo Host

**src/Quark.AwesomePizza.Silo/Program.cs**:
```csharp
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Actors;

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Awesome Pizza - Silo Host                         ║");
Console.WriteLine("║       Powered by Quark Framework                        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create actor factory
var factory = new ActorFactory();

Console.WriteLine("✅ Silo is ready");
Console.WriteLine();
Console.WriteLine("Commands:");
Console.WriteLine("  create <orderId> <customerId> <pizzaType>");
Console.WriteLine("  status <orderId> <newStatus>");
Console.WriteLine("  get <orderId>");
Console.WriteLine("  exit");
Console.WriteLine();

// Simple command loop
var activeActors = new Dictionary<string, OrderActor>();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
        continue;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
        continue;

    try
    {
        switch (parts[0].ToLower())
        {
            case "create" when parts.Length >= 4:
            {
                var orderId = parts[1];
                var customerId = parts[2];
                var pizzaType = string.Join(" ", parts.Skip(3));

                var actor = factory.CreateActor<OrderActor>(orderId);
                await actor.OnActivateAsync();
                activeActors[orderId] = actor;

                var order = await actor.CreateOrderAsync(customerId, pizzaType);
                Console.WriteLine($"   Order ID: {order.OrderId}");
                Console.WriteLine($"   Status: {order.Status}");
                break;
            }

            case "status" when parts.Length >= 3:
            {
                var orderId = parts[1];
                var statusString = parts[2];

                if (!activeActors.TryGetValue(orderId, out var actor))
                {
                    Console.WriteLine($"❌ Order {orderId} not found");
                    break;
                }

                if (!Enum.TryParse<OrderStatus>(statusString, true, out var newStatus))
                {
                    Console.WriteLine($"❌ Invalid status: {statusString}");
                    break;
                }

                var order = await actor.UpdateStatusAsync(newStatus);
                Console.WriteLine($"   Updated: {order.Status}");
                break;
            }

            case "get" when parts.Length >= 2:
            {
                var orderId = parts[1];

                if (!activeActors.TryGetValue(orderId, out var actor))
                {
                    Console.WriteLine($"❌ Order {orderId} not found");
                    break;
                }

                var order = await actor.GetOrderAsync();
                if (order != null)
                {
                    Console.WriteLine($"   Order ID: {order.OrderId}");
                    Console.WriteLine($"   Customer: {order.CustomerId}");
                    Console.WriteLine($"   Pizza: {order.PizzaType}");
                    Console.WriteLine($"   Status: {order.Status}");
                    Console.WriteLine($"   Created: {order.CreatedAt:HH:mm:ss}");
                }
                break;
            }

            case "exit":
            case "quit":
                Console.WriteLine("👋 Shutting down...");
                return;

            default:
                Console.WriteLine("❌ Unknown command");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
}
```

### Step 5: Create the Gateway API

**src/Quark.AwesomePizza.Gateway/Program.cs**:
```csharp
using System.Collections.Concurrent;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Actors;
using Quark.AwesomePizza.Shared.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory actor registry (simulates cluster client)
var factory = new ActorFactory();
var activeActors = new ConcurrentDictionary<string, OrderActor>();

app.MapGet("/", () => Results.Ok(new
{
    Service = "Awesome Pizza API",
    Version = "1.0.0",
    Endpoints = new[]
    {
        "POST /api/orders",
        "GET /api/orders/{orderId}",
        "PUT /api/orders/{orderId}/status"
    }
}));

app.MapPost("/api/orders", async (CreateOrderRequest request) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";
    var actor = factory.CreateActor<OrderActor>(orderId);
    
    await actor.OnActivateAsync();
    activeActors[orderId] = actor;

    var order = await actor.CreateOrderAsync(request.CustomerId, request.PizzaType);

    return Results.Created($"/api/orders/{orderId}", order);
});

app.MapGet("/api/orders/{orderId}", async (string orderId) =>
{
    if (!activeActors.TryGetValue(orderId, out var actor))
    {
        return Results.NotFound();
    }

    var order = await actor.GetOrderAsync();
    return order == null ? Results.NotFound() : Results.Ok(order);
});

app.MapPut("/api/orders/{orderId}/status", async (string orderId, UpdateStatusRequest request) =>
{
    if (!activeActors.TryGetValue(orderId, out var actor))
    {
        return Results.NotFound();
    }

    var order = await actor.UpdateStatusAsync(request.NewStatus);
    return Results.Ok(order);
});

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Awesome Pizza - Gateway API                       ║");
Console.WriteLine("║       http://localhost:5000                              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");

app.Run();

// Request/Response models
public record CreateOrderRequest(string CustomerId, string PizzaType);
public record UpdateStatusRequest(OrderStatus NewStatus);
```

### Step 6: Run the Demo

```bash
# Terminal 1: Start the Silo
cd /home/runner/work/Quark/Quark/productExample
dotnet run --project src/Quark.AwesomePizza.Silo

# Terminal 2: Start the Gateway
cd /home/runner/work/Quark/Quark/productExample
dotnet run --project src/Quark.AwesomePizza.Gateway

# Terminal 3: Test the API
# Create an order
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"customer-1","pizzaType":"Margherita"}'

# Get order status
curl http://localhost:5000/api/orders/{orderId}

# Update status
curl -X PUT http://localhost:5000/api/orders/{orderId}/status \
  -H "Content-Type: application/json" \
  -d '{"newStatus":"Preparing"}'
```

---

## ✅ Verification Checklist

After completing the quick start, verify:

- [ ] Redis is running (`docker ps` shows redis container)
- [ ] MQTT broker is running (`docker ps` shows mosquitto container)
- [ ] Solution builds without errors (`dotnet build`)
- [ ] Silo starts successfully (shows "Silo is ready")
- [ ] Gateway starts successfully (accessible at http://localhost:5000)
- [ ] Can create an order via API (returns 201 Created)
- [ ] Can retrieve order status (returns order details)
- [ ] Can update order status (status changes in response)

---

## 🔍 Troubleshooting

### Issue: Source Generator Not Working

**Symptom**: `No factory registered for actor type OrderActor`

**Solution**:
```bash
# Clean and rebuild
dotnet clean
dotnet build

# Verify generator reference in .csproj
# Must have OutputItemType="Analyzer" and ReferenceOutputAssembly="false"
```

### Issue: Redis Connection Failed

**Symptom**: "Connection refused" or "Unable to connect to Redis"

**Solution**:
```bash
# Check if Redis is running
docker ps | grep redis

# Restart Redis if needed
docker-compose restart redis

# Test Redis connection
docker exec -it awesomepizza-redis redis-cli ping
# Should return: PONG
```

### Issue: Port Already in Use

**Symptom**: "Address already in use" when starting gateway

**Solution**:
```bash
# Find process using port 5000
lsof -i :5000  # macOS/Linux
netstat -ano | findstr :5000  # Windows

# Kill the process or change port
# Edit appsettings.json or use --urls flag
dotnet run --project src/Quark.AwesomePizza.Gateway --urls "http://localhost:5001"
```

---

## 📚 Next Steps

Now that you have the basic demo working:

1. **Add Persistence**: Integrate Redis state storage
2. **Add Clustering**: Configure multi-silo deployment
3. **Add Real-time Tracking**: Implement MQTT bridge
4. **Add Kitchen Display**: Create KitchenActor and ChefActor
5. **Add Web UI**: Build React/Vue dashboard

Refer to the [Implementation Plan](01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md) for detailed roadmap.

---

## 🎓 Learning Resources

- **Quark Documentation**: `/home/runner/work/Quark/Quark/docs/`
- **Source Generator Setup**: `docs/SOURCE_GENERATOR_SETUP.md`
- **Existing Examples**: `examples/Quark.Demo.PizzaDash.Silo/`
- **Community Features**: `docs/COMMUNITY_FEATURES_ROADMAP.md`

---

**Happy Coding! 🍕🚀**

---

**Document Version**: 1.0  
**Last Updated**: 2026-01-31  
**Tested On**: .NET 10.0.102, Docker 27.x
