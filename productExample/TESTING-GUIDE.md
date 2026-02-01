# Architecture Fix - Testing Guide

## Overview

This document shows how to test the **corrected architecture** where Silos are the central actor host.

## What Changed

### Before (❌ Wrong)
```
MqttBridge (separate process) → Creates own actors
Gateway (separate process)    → Creates own actors  
Silo (isolated)                → Creates own actors

❌ 3 separate processes, each with duplicate actors
❌ No single source of truth
❌ Actors not properly distributed
```

### After (✅ Correct)
```
                    ┌─────────────────┐
                    │  Client Layer   │
                    │  (Gateway, UI)  │
                    └────────┬────────┘
                             │
                             │ gRPC/HTTP (TODO)
                             │ Actor Proxy Calls
                             │
    ┌────────────────────────▼────────────────────────┐
    │              SILO (Actor Host)                  │
    │                                                  │
    │  ┌──────────────────────────────────────────┐  │
    │  │     Actor System (Single Source)         │  │
    │  │                                           │  │
    │  │  OrderActor    DriverActor   ChefActor   │  │
    │  │  KitchenActor  InventoryActor  etc...    │  │
    │  │                                           │  │
    │  └──────────────────────────────────────────┘  │
    │                                                  │
    │  ┌──────────────┐      ┌─────────────────┐    │
    │  │ MQTT Service │      │  Actor Service  │    │
    │  │ (Integrated) │      │  (gRPC/HTTP)    │    │
    │  └───────▲──────┘      └─────────────────┘    │
    └──────────┼───────────────────────────────────┐─┘
               │                                     │
    ┌──────────▼──────────┐              ┌─────────▼────────┐
    │   MQTT Broker       │              │   Redis          │
    │   (IoT Messages)    │              │   (State)        │
    └─────────────────────┘              └──────────────────┘

✅ Single Silo process hosts ALL actors
✅ MQTT integrated INTO Silo
✅ Gateway will connect via proxy (TODO)
✅ Single source of truth
```

## Testing the Architecture

### Step 1: Start Infrastructure

```bash
# Start Redis and MQTT broker
cd productExample
docker compose up -d

# Verify containers are running
docker ps
# Should show: awesomepizza-redis, awesomepizza-mqtt
```

### Step 2: Start the Silo (Actor Host)

```bash
cd src/Quark.AwesomePizza.Silo
dotnet run
```

**Expected Output:**
```
╔══════════════════════════════════════════════════════════╗
║       Awesome Pizza - Quark Silo Host                   ║
║       Central Actor System with MQTT Integration         ║
╚══════════════════════════════════════════════════════════╝

🏭 Silo ID: silo-xxxxx
🔌 Redis:   localhost:6379
🔌 MQTT:    localhost:1883
⚡ Native AOT: Enabled
🚀 Started at: 2026-02-01 XX:XX:XX UTC

🔌 MQTT Client ID: awesomepizza-silo-xxxxx
🔌 MQTT Broker: localhost:1883
⏳ Connecting to MQTT broker...
✅ MQTT: Connected to broker
✅ MQTT: Subscribed to topics
   • pizza/drivers/+/location
   • pizza/drivers/+/status
   • pizza/kitchen/+/oven
   • pizza/kitchen/+/alerts
   • pizza/orders/+/events

✅ Silo is ready - All actors live here!
📋 Actor types: Order, Driver, Chef, Kitchen, Inventory, Restaurant

💡 Architecture:
   • Silo = Central actor host (YOU ARE HERE)
   • Gateway = Connects to actors via proxy calls
   • MQTT = Updates actors directly in this Silo

Commands:
  create-order <orderId> <customerId> <restaurantId>
  create-driver <driverId> <name>
  create-chef <chefId> <name>
  status <orderId> <newStatus>
  list
  exit

>
```

### Step 3: Test Actor Creation in Silo

In the Silo console, create some actors:

```bash
> create-driver driver-1 "John Doe"
✅ Driver created: driver-1
   Name: John Doe
   Status: Available

> create-order order-1 customer-1 restaurant-1
✅ Order created: order-1
   Customer: customer-1
   Restaurant: restaurant-1
   Status: Created
   Total: $12.99
   ETA: 12:45:00

> list
📋 Active actors on this silo: 2
   • DriverActor: driver-1
   • OrderActor: order-1
```

### Step 4: Test MQTT → Silo → Actor Flow

**Terminal 1: Keep Silo running**

**Terminal 2: Publish MQTT message**
```bash
# Using docker exec to publish to local broker
docker exec awesomepizza-mqtt mosquitto_pub \
  -t "pizza/drivers/driver-1/location" \
  -m '{"lat":40.7128,"lon":-74.0060}'
```

**Expected in Silo console:**
```
📩 MQTT: pizza/drivers/driver-1/location
   ✅ Updated location for driver-1: (40.7128, -74.0060)
```

**Publish status update:**
```bash
docker exec awesomepizza-mqtt mosquitto_pub \
  -t "pizza/drivers/driver-1/status" \
  -m '{"status":"Busy"}'
```

**Expected in Silo console:**
```
📩 MQTT: pizza/drivers/driver-1/status
   ✅ Updated status for driver-1: Busy
```

### Step 5: Test Gateway (Current State)

**Terminal 3: Start Gateway**
```bash
cd src/Quark.AwesomePizza.Gateway
dotnet run
```

**Expected Output:**
```
╔══════════════════════════════════════╗
║  Awesome Pizza - Gateway API         ║
║  REST API connecting to Silo         ║
╚══════════════════════════════════════╝

⚠️  NOTE: This gateway should connect to Silo
    For now, it creates local actors (demo mode)
    In production: Use IClusterClient or gRPC

Gateway API starting on: http://localhost:5000
```

**Terminal 4: Test API**
```bash
# Create order via Gateway API
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "customer-2",
    "restaurantId": "restaurant-1",
    "items": [{
      "pizzaType": "Margherita",
      "size": "Large",
      "toppings": ["cheese"],
      "quantity": 1,
      "price": 12.99
    }],
    "deliveryAddress": {
      "latitude": 40.7128,
      "longitude": -74.0060,
      "timestamp": "2026-02-01T00:00:00Z"
    }
  }'
```

**⚠️ Current Limitation:**
The Gateway currently creates actors locally. In the correct architecture, this should:
1. Gateway makes gRPC call to Silo
2. Silo creates the actor
3. Silo returns actor reference
4. Future Gateway calls use that reference

## Architecture Validation

### ✅ What Works Now

1. **Silo as Central Host**
   - ✅ All actors created in Silo
   - ✅ Single actor factory
   - ✅ Actors managed centrally

2. **MQTT Integration**
   - ✅ MQTT service runs inside Silo
   - ✅ MQTT messages update actors directly
   - ✅ No separate bridge process

3. **Actor Lifecycle**
   - ✅ Actors created on-demand
   - ✅ State maintained in Silo
   - ✅ Command interface for testing

### ⚠️ What Needs Implementation

1. **Gateway-to-Silo Communication**
   - ⚠️ Gateway should call Silo via gRPC/HTTP
   - ⚠️ Actor proxy pattern needed
   - ⚠️ Currently Gateway creates local actors

2. **Distributed Cluster**
   - ⚠️ Multiple Silo support
   - ⚠️ Actor placement strategy
   - ⚠️ Redis clustering

3. **State Persistence**
   - ⚠️ Redis integration for state
   - ⚠️ ETags for optimistic concurrency
   - ⚠️ Actor reminders

## Key Takeaways

### Architectural Principle
**"Silos are the actor center. Everything else connects to actors IN the Silo."**

### Component Roles

1. **Silo** (Actor Host)
   - Creates and manages ALL actors
   - Hosts MQTT service for IoT updates
   - Manages actor lifecycle
   - Persists state to Redis
   - **Status**: ✅ Implemented

2. **Gateway** (API Layer)
   - Exposes REST API
   - Connects to actors in Silo via proxy
   - Handles HTTP/WebSocket/SSE for clients
   - **Status**: ⚠️ Needs proxy implementation

3. **MQTT Broker** (Message Queue)
   - Receives IoT messages
   - Broker only - no actor logic
   - **Status**: ✅ Running

4. **Redis** (State Storage)
   - Stores actor state
   - Clustering metadata
   - **Status**: ✅ Running (not yet integrated)

## Next Steps

1. **Implement Actor Proxy in Gateway**
   ```csharp
   // Gateway should do:
   var siloClient = new SiloGrpcClient("localhost:7000");
   var orderActor = await siloClient.GetActorAsync<OrderActor>(orderId);
   var result = await orderActor.CreateOrderAsync(request);
   ```

2. **Expose gRPC Service in Silo**
   ```csharp
   // Silo should expose:
   service ActorService {
     rpc CreateOrder (CreateOrderRequest) returns (OrderState);
     rpc GetOrder (GetOrderRequest) returns (OrderState);
     rpc UpdateDriver (UpdateDriverRequest) returns (DriverState);
   }
   ```

3. **Integration Tests**
   - Test full flow: Gateway → Silo → Actor → Redis
   - Test MQTT: IoT Device → MQTT → Silo → Actor
   - Test distributed: Multiple Silos with actor routing

## Conclusion

The architecture has been **corrected** to follow the distributed actor pattern:

- ✅ Silos are the central actor host
- ✅ MQTT is integrated into Silo
- ⚠️ Gateway needs proxy implementation (next step)

This lays the foundation for a true distributed actor system where Silos can be scaled horizontally and actors can be distributed across a cluster.

---

**Last Updated**: 2026-02-01  
**Status**: Core architecture corrected, Gateway proxy pending
