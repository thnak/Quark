# Architecture Fix: Silos as Central Actor Host

## Problem Statement

The original implementation did not follow the correct distributed actor architecture concept:

- ❌ **MqttBridge** created its own actor instances
- ❌ **Gateway** created its own actor instances  
- ❌ **Silo** had actors but was isolated
- ❌ Actors were duplicated across multiple processes
- ❌ No single source of truth for actor state

## Correct Architecture

### Core Concept: Silos are the Actor Center

```
┌─────────────────────────────────────────────────────────────┐
│                         CLIENT LAYER                        │
│  ┌─────────────┐           ┌──────────────┐                │
│  │  Gateway    │           │ MQTT Broker  │                │
│  │  (HTTP/SSE) │           │ (Messages)   │                │
│  └──────┬──────┘           └───────┬──────┘                │
│         │                          │                        │
│         │ gRPC/HTTP               │ MQTT                   │
│         │ Actor Proxy             │ Publish                │
│         │                          │                        │
└─────────┼──────────────────────────┼────────────────────────┘
          │                          │
          ▼                          ▼
┌─────────────────────────────────────────────────────────────┐
│                     ACTOR HOST (SILO)                       │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │          Actor System (Central Authority)            │  │
│  │                                                       │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐          │  │
│  │  │OrderActor│  │DriverAct │  │ChefActor │          │  │
│  │  └──────────┘  └──────────┘  └──────────┘          │  │
│  │                                                       │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐          │  │
│  │  │KitchenAct│  │Inventory │  │Restaurant│          │  │
│  │  └──────────┘  └──────────┘  └──────────┘          │  │
│  │                                                       │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌────────────────────┐     ┌─────────────────────┐        │
│  │   MQTT Service     │     │   Actor Service     │        │
│  │   (Integrated)     │     │   (gRPC/HTTP)       │        │
│  └────────────────────┘     └─────────────────────┘        │
│                                                              │
└──────────────────────────────┬───────────────────────────────┘
                               │
                               ▼
                         ┌──────────┐
                         │  Redis   │
                         │  (State) │
                         └──────────┘
```

## Implementation Changes

### 1. ✅ Silo: Central Actor Host

**File**: `src/Quark.AwesomePizza.Silo/Program.cs`

- **Added**: MQTT service integration
- **Added**: Actor service for external access
- All actors are created and managed here
- MQTT messages update actors directly in the Silo
- Provides actor access methods for Gateway

**Key Changes**:
```csharp
// MQTT service runs inside Silo
_mqttService = new MqttService(_factory, ActiveActors, mqttHost, mqttPort);
await _mqttService.StartAsync();

// Actors are accessible to external clients
public static async Task<T?> GetOrCreateActorAsync<T>(string actorId) where T : IActor
```

### 2. ✅ MQTT Integration in Silo

**File**: `src/Quark.AwesomePizza.Silo/MqttService.cs`

- MQTT client runs inside the Silo process
- Receives MQTT messages and calls actor methods directly
- No separate MQTT bridge process needed
- Actors are updated in real-time from IoT devices

**Flow**:
```
IoT Device → MQTT Broker → Silo MQTT Service → DriverActor.UpdateLocationAsync()
                                             → KitchenActor.UpdateTemperature()
```

### 3. ⚠️ Gateway: Should Connect to Silo (TODO)

**File**: `src/Quark.AwesomePizza.Gateway/Program.cs`

**Current State**: Still creates local actors (for demo simplicity)

**Correct Pattern** (To implement):
```csharp
// Instead of:
var actor = actorFactory.CreateActor<OrderActor>(orderId);

// Should be:
var siloClient = new SiloClient("localhost:7000"); // gRPC endpoint
var orderActor = await siloClient.GetActorAsync<OrderActor>(orderId);
```

**Why not implemented yet**: 
- Requires gRPC or HTTP client setup
- Needs Silo to expose actor methods via API
- Current demo mode shows the concept

### 4. ❌ MqttBridge: Deprecated

**File**: `src/Quark.AwesomePizza.MqttBridge/` (Keep for reference, not used)

- This separate project is NO LONGER NEEDED
- MQTT functionality is now integrated into Silo
- Kept in repo for educational comparison

## Testing the Architecture

### Start the Silo (Actor Host)
```bash
cd src/Quark.AwesomePizza.Silo
dotnet run
```

You should see:
```
✅ Silo is ready - All actors live here!
✅ MQTT: Connected to broker
✅ MQTT: Subscribed to topics
```

### Test MQTT → Silo → Actor Flow
```bash
# Terminal 2: Send MQTT message
mosquitto_pub -t "pizza/drivers/driver-1/location" -m '{"lat":40.7128,"lon":-74.0060}'
```

In Silo console, you'll see:
```
📩 MQTT: pizza/drivers/driver-1/location
   🆕 Created actor: DriverActor (driver-1)
   ✅ Updated location for driver-1: (40.7128, -74.0060)
```

### Test Gateway → Silo (Currently Local)
```bash
# Terminal 3: Start Gateway
cd src/Quark.AwesomePizza.Gateway
dotnet run

# Terminal 4: Create order via Gateway
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "customer-1",
    "restaurantId": "restaurant-1",
    "items": [{"pizzaType": "Margherita", "size": "Large", "toppings": ["cheese"], "quantity": 1, "price": 12.99}],
    "deliveryAddress": {"latitude": 40.7128, "longitude": -74.0060, "timestamp": "2026-02-01T00:00:00Z"}
  }'
```

⚠️ **Note**: Gateway currently creates local actors. This should be updated to call Silo's actor service.

## Benefits of This Architecture

### ✅ Single Source of Truth
- All actors live in the Silo
- No duplicate actor instances
- Consistent state across all clients

### ✅ True Distributed System
- Silo can be scaled horizontally
- Actors can migrate between Silos
- Proper actor lifecycle management

### ✅ Clean Separation of Concerns
- **Silo**: Actor hosting and business logic
- **Gateway**: HTTP API and client communication
- **MQTT**: IoT integration (runs inside Silo)

### ✅ Scalability
- Multiple Silos can form a cluster
- Actors are distributed via consistent hashing
- Load balancing at the actor level

## Next Steps (TODO)

### 1. Implement Silo-to-Gateway Communication
- [ ] Add gRPC service to Silo
- [ ] Expose actor methods via gRPC
- [ ] Gateway uses gRPC client to call actors
- [ ] Remove local actor creation from Gateway

### 2. Implement Actor Clustering
- [ ] Use Redis for cluster membership
- [ ] Implement actor placement strategy
- [ ] Support multiple Silo instances
- [ ] Actor migration on Silo failure

### 3. State Persistence
- [ ] Integrate Redis storage
- [ ] Implement optimistic concurrency with ETags
- [ ] Add actor reminders
- [ ] Event sourcing for order history

### 4. Production Hardening
- [ ] Add health checks
- [ ] Implement circuit breakers
- [ ] Add telemetry (OpenTelemetry)
- [ ] Load testing

## Summary

This architecture fix ensures that:

1. ✅ **Silo is the central actor host** - All actors live here
2. ✅ **MQTT integrates with Silo** - Updates actors directly
3. ⚠️ **Gateway connects to Silo** - Should use proxies (TODO)
4. ❌ **No separate MQTT bridge** - Functionality moved to Silo

The architecture now correctly follows the distributed actor pattern where Silos are the authoritative source for all actor state and behavior.

---

**Last Updated**: 2026-02-01  
**Status**: Core concept implemented, Gateway integration pending
