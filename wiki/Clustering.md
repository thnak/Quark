# Clustering

Quark provides distributed actor capabilities through Redis-based cluster membership and gRPC transport. This enables location transparency where actors can be distributed across multiple nodes (silos) while appearing as local objects to the developer.

## Overview

A Quark cluster consists of multiple **silos** (nodes) that work together to host actors:

- **Cluster Membership** - Redis-based service discovery and health monitoring
- **Actor Directory** - Tracks which silo hosts each actor instance
- **Consistent Hashing** - Deterministic actor placement across silos
- **Location Transparency** - Call actors without knowing their physical location
- **gRPC Transport** - High-performance communication between silos

## Architecture

```
┌─────────────┐           ┌─────────────┐           ┌─────────────┐
│   Silo 1    │           │   Silo 2    │           │   Silo 3    │
│             │◄─────────►│             │◄─────────►│             │
│ Actor A, B  │   gRPC    │ Actor C, D  │   gRPC    │ Actor E, F  │
└──────┬──────┘           └──────┬──────┘           └──────┬──────┘
       │                         │                         │
       └─────────────┬───────────┴─────────────────────────┘
                     │
              ┌──────▼──────┐
              │    Redis    │
              │ (Membership │
              │ + Directory)│
              └─────────────┘
```

## Core Interfaces

### IClusterMembership

Manages cluster membership and health monitoring.

```csharp
namespace Quark.Abstractions.Clustering;

public interface IClusterMembership
{
    /// <summary>
    /// Gets the current silo ID.
    /// </summary>
    string CurrentSiloId { get; }

    /// <summary>
    /// Registers this silo in the cluster.
    /// </summary>
    Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters this silo from the cluster.
    /// </summary>
    Task UnregisterSiloAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active silos in the cluster.
    /// </summary>
    Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat timestamp for this silo.
    /// </summary>
    Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a silo joins the cluster.
    /// </summary>
    event EventHandler<SiloInfo>? SiloJoined;

    /// <summary>
    /// Event raised when a silo leaves the cluster.
    /// </summary>
    event EventHandler<SiloInfo>? SiloLeft;
}
```

**Key Operations:**
- **RegisterSiloAsync** - Announces this silo to the cluster
- **UpdateHeartbeatAsync** - Sends periodic heartbeat to prove liveness
- **GetActiveSilosAsync** - Retrieves all healthy cluster members
- **SiloJoined/SiloLeft Events** - React to topology changes

### IActorDirectory

Tracks actor placement and enables routing.

```csharp
public interface IActorDirectory
{
    /// <summary>
    /// Registers an actor's location in the directory.
    /// </summary>
    Task RegisterActorAsync(ActorLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the location of an actor.
    /// </summary>
    Task<ActorLocation?> LookupActorAsync(string actorId, string actorType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters an actor from the directory.
    /// </summary>
    Task UnregisterActorAsync(string actorId, string actorType, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all actors on a specific silo.
    /// </summary>
    Task<IReadOnlyCollection<ActorLocation>> GetActorsBySiloAsync(string siloId,
        CancellationToken cancellationToken = default);
}
```

**ActorLocation** contains:
- `ActorId` - The actor's unique identifier
- `ActorType` - The actor's type name
- `SiloId` - Which silo hosts this actor
- `LastUpdated` - Timestamp for staleness detection

### IActorTransport

Handles cross-silo actor invocations (gRPC implementation).

```csharp
public interface IActorTransport
{
    /// <summary>
    /// Invokes a method on a remote actor.
    /// </summary>
    Task<byte[]?> InvokeActorAsync(
        string targetSiloId,
        string actorType,
        string actorId,
        string methodName,
        byte[]? arguments,
        CancellationToken cancellationToken = default);
}
```

## Consistent Hashing

Quark uses consistent hashing to determine actor placement:

1. **Deterministic Placement** - Same `actorId` always maps to the same silo
2. **Minimal Disruption** - Adding/removing silos only affects a fraction of actors
3. **Load Distribution** - Actors are evenly distributed across silos

The hashing algorithm:
```csharp
// Simplified concept (actual implementation in Quark.Clustering.Redis)
string ChooseSilo(string actorId, List<SiloInfo> silos)
{
    var hash = ComputeHash(actorId);
    var index = hash % silos.Count;
    return silos[index].SiloId;
}
```

## Configuration

### Redis Cluster Membership

```csharp
using Quark.Clustering.Redis;
using StackExchange.Redis;

// Create Redis connection
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

// Create cluster membership
var membership = new RedisClusterMembership(
    redis,
    siloId: "silo-1",
    heartbeatIntervalSeconds: 5,
    expirationSeconds: 15
);

// Register silo
var siloInfo = new SiloInfo
{
    SiloId = "silo-1",
    Address = "10.0.0.1",
    Port = 5000,
    JoinedAt = DateTimeOffset.UtcNow
};

await membership.RegisterSiloAsync(siloInfo);
await membership.StartAsync(); // Start heartbeat and monitoring
```

**Configuration Parameters:**
- **heartbeatIntervalSeconds** - How often to send heartbeats (default: 5s)
- **expirationSeconds** - When to consider a silo dead (default: 15s)
- **Address/Port** - Network endpoint for gRPC communication

### gRPC Transport

```csharp
using Quark.Transport.Grpc;
using Grpc.Net.Client;

// Create gRPC transport
var transport = new GrpcActorTransport(
    currentSiloId: "silo-1",
    membership: membership,
    channelFactory: targetSilo => 
    {
        return GrpcChannel.ForAddress($"http://{targetSilo.Address}:{targetSilo.Port}");
    }
);

// Transport automatically routes calls to the correct silo
```

## Location Transparency

The actor factory automatically handles distributed routing:

```csharp
// Create an actor - might be local or remote
var actor = factory.CreateActor<OrderActor>("order-123");

// Call methods - routing is transparent
await actor.OnActivateAsync();
actor.ProcessOrder(orderData);

// Quark automatically:
// 1. Looks up actor location in the directory
// 2. Routes to local instance OR
// 3. Serializes call and sends via gRPC to remote silo
```

## Example: Multi-Silo Setup

### Silo 1 (Entry Point)

```csharp
using Quark.Clustering.Redis;
using Quark.Transport.Grpc;
using Quark.Core.Actors;
using StackExchange.Redis;

// Setup Redis connection
var redis = await ConnectionMultiplexer.ConnectAsync("redis-server:6379");

// Create cluster membership
var membership = new RedisClusterMembership(
    redis,
    siloId: "silo-1",
    heartbeatIntervalSeconds: 5,
    expirationSeconds: 15
);

// Create actor directory
var directory = new RedisActorDirectory(redis);

// Create gRPC transport
var transport = new GrpcActorTransport(
    currentSiloId: "silo-1",
    membership: membership,
    channelFactory: silo => GrpcChannel.ForAddress($"http://{silo.Address}:{silo.Port}")
);

// Create distributed actor factory
var factory = new DistributedActorFactory(
    localFactory: new ActorFactory(),
    directory: directory,
    transport: transport,
    membership: membership
);

// Register this silo
await membership.RegisterSiloAsync(new SiloInfo
{
    SiloId = "silo-1",
    Address = "10.0.0.1",
    Port = 5000,
    JoinedAt = DateTimeOffset.UtcNow
});

// Start heartbeat and monitoring
await membership.StartAsync();

// Use actors - distributed automatically
var userActor = factory.CreateActor<UserActor>("user-123");
await userActor.OnActivateAsync();
await userActor.UpdateProfileAsync(profile);
```

### Silo 2 (Worker Node)

```csharp
// Same setup with different SiloId and Address
var membership = new RedisClusterMembership(
    redis,
    siloId: "silo-2",
    heartbeatIntervalSeconds: 5,
    expirationSeconds: 15
);

await membership.RegisterSiloAsync(new SiloInfo
{
    SiloId = "silo-2",
    Address = "10.0.0.2",
    Port = 5000,
    JoinedAt = DateTimeOffset.UtcNow
});

await membership.StartAsync();
// Silo 2 is now part of the cluster and can host actors
```

## Actor Placement Strategy

Quark follows an **activation-on-first-use** model:

1. **First Call** - Actor doesn't exist anywhere
   - Consistent hash determines target silo
   - Actor is activated on that silo
   - Location registered in directory

2. **Subsequent Calls** - Actor exists
   - Directory lookup finds existing location
   - Calls routed to that silo
   - No re-placement unless silo fails

3. **Silo Failure** - Hosting silo goes down
   - Actor location becomes stale
   - Next call triggers re-activation on a new silo
   - Directory updated with new location

## Monitoring Cluster Health

```csharp
// Subscribe to membership events
membership.SiloJoined += (sender, silo) =>
{
    Console.WriteLine($"Silo joined: {silo.SiloId} at {silo.Address}:{silo.Port}");
};

membership.SiloLeft += (sender, silo) =>
{
    Console.WriteLine($"Silo left: {silo.SiloId}");
    // Trigger actor rebalancing if needed
};

// Query cluster topology
var activeSilos = await membership.GetActiveSilosAsync();
Console.WriteLine($"Cluster has {activeSilos.Count} active silos:");
foreach (var silo in activeSilos)
{
    Console.WriteLine($"  - {silo.SiloId}: {silo.Address}:{silo.Port}");
}

// Check actor distribution
var actorsOnSilo1 = await directory.GetActorsBySiloAsync("silo-1");
Console.WriteLine($"Silo 1 hosts {actorsOnSilo1.Count} actors");
```

## Best Practices

### 1. Network Configuration

- **Use persistent connections** - gRPC channels are reused
- **Configure timeouts** - Set appropriate deadlines for cross-silo calls
- **Handle network partitions** - Implement retry logic with exponential backoff

### 2. Actor Affinity

- **Co-locate related actors** - Use consistent ID prefixes for locality
  ```csharp
  // These will likely land on the same silo
  var order = factory.CreateActor<OrderActor>("customer-123/order-456");
  var payment = factory.CreateActor<PaymentActor>("customer-123/payment-789");
  ```

### 3. Scalability

- **Horizontal scaling** - Add silos to increase capacity
- **Monitor distribution** - Ensure actors are evenly spread
- **Avoid hotspots** - Don't use sequential IDs (e.g., "actor-1", "actor-2", ...)

### 4. Resilience

- **Heartbeat tuning** - Balance detection speed vs. false positives
  - Short interval (3-5s): Fast failure detection, more Redis traffic
  - Long interval (15-30s): Less overhead, slower detection

- **Grace periods** - Allow silos time to recover from transient issues

### 5. Redis Configuration

- **High availability** - Use Redis Sentinel or Cluster for production
- **Persistence** - Enable AOF/RDB to survive Redis restarts
- **Memory limits** - Set appropriate maxmemory and eviction policies
- **Network** - Low latency between silos and Redis (same datacenter)

## Troubleshooting

### Actors Not Found

**Symptom:** Actors exist but can't be located

**Causes:**
- Directory lookup failing
- Stale actor locations after silo restart
- Redis connectivity issues

**Solutions:**
```csharp
// Verify directory connectivity
var location = await directory.LookupActorAsync("actor-1", "MyActor");
if (location == null)
{
    Console.WriteLine("Actor not found in directory");
}

// Manually re-register if needed
await directory.RegisterActorAsync(new ActorLocation(
    actorId: "actor-1",
    actorType: "MyActor",
    siloId: membership.CurrentSiloId
));
```

### Cluster Split-Brain

**Symptom:** Multiple silos think they own the same actor

**Cause:** Network partition separating silos from Redis

**Prevention:**
- Use Redis Sentinel for automatic failover
- Implement proper timeout handling
- Monitor Redis connectivity

### Performance Issues

**Symptom:** Slow cross-silo calls

**Diagnostics:**
```csharp
// Measure round-trip time
var sw = Stopwatch.StartNew();
await remoteActor.SomeMethodAsync();
Console.WriteLine($"Cross-silo call took: {sw.ElapsedMilliseconds}ms");

// Check if actor is local or remote
var location = await directory.LookupActorAsync(actorId, actorType);
var isLocal = location?.SiloId == membership.CurrentSiloId;
Console.WriteLine($"Actor is {(isLocal ? "local" : "remote")}");
```

**Solutions:**
- Reduce call frequency (batch operations)
- Consider actor co-location
- Use streaming for high-throughput scenarios
- Check network latency between silos

## Related Topics

- **[Actor Model](Actor-Model)** - Core actor concepts
- **[Persistence](Persistence)** - State storage in distributed scenarios
- **[Streaming](Streaming)** - Distributed pub/sub patterns
- **[API Reference](API-Reference)** - Clustering interfaces reference
- **[Getting Started](Getting-Started)** - Basic setup before clustering

---

**Next Steps:**
- Try the clustering example (coming in Phase 6)
- Learn about [Timers and Reminders](Timers-and-Reminders) for scheduled work
- Explore [Streaming](Streaming) for distributed event processing
