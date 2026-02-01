# Feature Request: IClusterClient Implementation

## Status
**Priority**: HIGH  
**Type**: Core Feature  
**Component**: Quark.Client

## Problem Statement

The Quark Framework currently lacks a proper `IClusterClient` implementation for remote actor invocation. This is a critical feature needed for distributed actor systems where:

1. **Gateway** needs to call actors hosted in Silo without direct instantiation
2. **MQTT Bridge** needs to update actors in Silo remotely
3. **Multiple Silos** need to communicate with actors across the cluster

Currently, clients must either:
- Directly instantiate actors (breaks distributed model)
- Manually implement gRPC/HTTP communication (reinventing the wheel)

## Required Features

### 1. IClusterClient Interface

```csharp
namespace Quark.Client;

/// <summary>
/// Client for connecting to Quark actor cluster and invoking actors remotely.
/// </summary>
public interface IClusterClient
{
    /// <summary>
    /// Gets a proxy reference to an actor of type T with the specified ID.
    /// </summary>
    T GetActor<T>(string actorId) where T : class;
    
    /// <summary>
    /// Gets a proxy reference to an actor of type T with the specified ID.
    /// </summary>
    Task<T> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Connects to the cluster.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects from the cluster.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Checks if the client is connected to the cluster.
    /// </summary>
    bool IsConnected { get; }
}
```

### 2. Actor Proxy Generation

The client should generate proxies for actor interfaces that:
- Serialize method calls to the wire format (gRPC/MessagePack)
- Route calls to the correct Silo hosting the actor
- Handle retries and failover
- Support async/await patterns

```csharp
// Example usage:
var client = serviceProvider.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

var orderActor = client.GetActor<IOrderActor>("order-123");
var result = await orderActor.CreateOrderAsync(request);
```

### 3. Cluster Connection Configuration

```csharp
public class ClusterClientOptions
{
    /// <summary>
    /// Connection string or list of Silo endpoints.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:7000";
    
    /// <summary>
    /// Cluster ID for multi-cluster scenarios.
    /// </summary>
    public string ClusterId { get; set; } = "default";
    
    /// <summary>
    /// Service ID for the application.
    /// </summary>
    public string ServiceId { get; set; } = "awesome-pizza";
    
    /// <summary>
    /// Transport protocol (gRPC, TCP, etc.).
    /// </summary>
    public ClusterTransport Transport { get; set; } = ClusterTransport.Grpc;
    
    /// <summary>
    /// Retry policy for failed calls.
    /// </summary>
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;
}
```

### 4. DI Registration Extension

```csharp
public static class ClusterClientServiceCollectionExtensions
{
    public static IServiceCollection AddClusterClient(
        this IServiceCollection services,
        Action<ClusterClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IClusterClient, ClusterClient>();
        return services;
    }
}
```

## Implementation Approach

### Option 1: Source Generator (Recommended)

Use Roslyn source generators to create actor proxies at compile time:

```csharp
// User writes:
public interface IOrderActor
{
    Task<OrderState> CreateOrderAsync(CreateOrderRequest request);
}

// Generator creates:
internal class OrderActorProxy : IOrderActor
{
    private readonly IClusterClient _client;
    private readonly string _actorId;
    
    public async Task<OrderState> CreateOrderAsync(CreateOrderRequest request)
    {
        return await _client.InvokeAsync<OrderState>(
            _actorId,
            nameof(CreateOrderAsync),
            new object[] { request });
    }
}
```

**Benefits**:
- Zero reflection at runtime (AOT compatible)
- Compile-time safety
- Better performance
- Aligns with Quark's zero-reflection philosophy

### Option 2: Dynamic Proxy (Castle.DynamicProxy)

Use runtime proxy generation:

**Drawbacks**:
- Requires reflection
- Not AOT compatible
- Against Quark's design principles

## Wire Protocol

### Recommended: gRPC

```protobuf
service ActorService {
  rpc InvokeActor (ActorInvocationRequest) returns (ActorInvocationResponse);
  rpc StreamActorEvents (StreamRequest) returns (stream ActorEvent);
}

message ActorInvocationRequest {
  string actor_id = 1;
  string actor_type = 2;
  string method_name = 3;
  bytes serialized_args = 4;
}

message ActorInvocationResponse {
  bytes serialized_result = 1;
  string error_message = 2;
}
```

### Alternative: Custom TCP Protocol

For maximum performance, implement custom binary protocol using MessagePack or ProtoBuf-net.

## Actor Routing

The client needs to discover which Silo hosts a given actor:

1. **Redis-based Directory**: Use Redis as actor location directory
   ```
   Key: actor:{actorId}
   Value: {siloEndpoint}
   TTL: 300 seconds
   ```

2. **Consistent Hashing**: Use consistent hashing to deterministically route actors
3. **Silo Gateway**: Query Silo for actor location

## Example Usage in Projects

### Silo (Actor Host)

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

// Register actors
builder.Services.AddActorSystem(options => {
    options.UseRedisStateStorage("localhost:6379");
    options.UseRedisClustering("localhost:6379");
});

// Register gRPC service for remote calls
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<ActorGrpcService>();
app.Run();
```

### Gateway

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

// Register cluster client
builder.Services.AddClusterClient(options => {
    options.ConnectionString = "localhost:7000";
    options.ClusterId = "awesome-pizza-cluster";
    options.ServiceId = "gateway";
});

var app = builder.Build();

// Connect to cluster
var client = app.Services.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

// Use in endpoints
app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IClusterClient client) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";
    var orderActor = client.GetActor<IOrderActor>(orderId);
    var response = await orderActor.CreateOrderAsync(request);
    return Results.Created($"/api/orders/{orderId}", response);
});

app.Run();
```

### MQTT Bridge

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddClusterClient(options => {
    options.ConnectionString = "localhost:7000";
});

builder.Services.AddSingleton<IMqttClient>(sp => {
    var factory = new MqttFactory();
    return factory.CreateMqttClient();
});

builder.Services.AddHostedService<MqttBridgeService>();

var app = builder.Build();
app.Run();

// In MqttBridgeService:
public class MqttBridgeService : BackgroundService
{
    private readonly IClusterClient _clusterClient;
    private readonly IMqttClient _mqttClient;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _clusterClient.ConnectAsync(stoppingToken);
        
        await _mqttClient.SubscribeAsync("pizza/drivers/+/location");
        
        _mqttClient.ApplicationMessageReceivedAsync += async e => {
            var driverId = ParseDriverId(e.ApplicationMessage.Topic);
            var driverActor = _clusterClient.GetActor<IDriverActor>(driverId);
            await driverActor.UpdateLocationAsync(lat, lon, timestamp);
        };
    }
}
```

## Benefits

1. **True Distributed System**: Actors can be hosted across multiple Silos
2. **Clean Architecture**: Clear separation between actor interface and implementation
3. **Scalability**: Clients connect to cluster, not individual Silos
4. **Location Transparency**: Clients don't need to know where actors are hosted
5. **Fault Tolerance**: Automatic retry and failover
6. **Type Safety**: Compile-time checking via interfaces

## Alternatives (Workarounds)

### Temporary Workaround: In-Process Actor Service

For the current implementation, we can create a simple in-process actor service:

```csharp
public interface IActorService
{
    T GetActor<T>(string actorId) where T : class;
}

public class InProcessActorService : IActorService
{
    private readonly IActorFactory _factory;
    private readonly Dictionary<string, IActor> _actors = new();
    
    public T GetActor<T>(string actorId) where T : class
    {
        if (!_actors.TryGetValue(actorId, out var actor))
        {
            actor = _factory.CreateActor<T>(actorId);
            _actors[actorId] = actor;
        }
        return (T)actor;
    }
}
```

**Limitations**:
- Only works in-process
- No distribution
- Not suitable for production

## Timeline

**Phase 1** (2-3 weeks): Basic IClusterClient interface and source generator
**Phase 2** (2-3 weeks): gRPC transport implementation
**Phase 3** (1-2 weeks): Redis-based actor routing
**Phase 4** (1 week): Integration tests and documentation

## Dependencies

- Grpc.AspNetCore (for gRPC transport)
- Microsoft.CodeAnalysis (for source generator)
- StackExchange.Redis (for actor routing)

## Related Issues

- #XXX: Add clustering support
- #XXX: Implement state management
- #XXX: Add actor reminders

## Acceptance Criteria

- [ ] IClusterClient interface defined
- [ ] Source generator creates actor proxies
- [ ] gRPC transport implemented
- [ ] Actors can be invoked remotely
- [ ] DI registration extensions provided
- [ ] Integration tests passing
- [ ] Documentation complete
- [ ] AOT compatible (zero reflection)

---

**Created**: 2026-02-01  
**Author**: Copilot Agent  
**Status**: PENDING IMPLEMENTATION
