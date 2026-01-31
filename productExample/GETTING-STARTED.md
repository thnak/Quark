# Awesome Pizza - Getting Started

This guide will help you run the Awesome Pizza demo application.

## Prerequisites

- .NET 10 SDK (10.0.102 or later)
- Docker Desktop (for Redis and MQTT)

## Quick Start

### 1. Start Infrastructure

Start Redis and MQTT broker using Docker Compose:

```bash
cd productExample
docker-compose up -d
```

Verify services are running:

```bash
docker-compose ps
```

You should see both `awesomepizza-redis` and `awesomepizza-mqtt` running.

### 2. Build the Solution

```bash
# Build all projects
cd productExample/src
dotnet build Quark.AwesomePizza.Shared
dotnet build Quark.AwesomePizza.Silo
dotnet build Quark.AwesomePizza.Gateway
dotnet build Quark.AwesomePizza.MqttBridge
```

### 3. Start the Silo (Actor Host)

Open a terminal and start the Silo:

```bash
cd productExample/src/Quark.AwesomePizza.Silo
dotnet run
```

You should see:

```
╔══════════════════════════════════════════════════════════╗
║       Awesome Pizza - Quark Silo Host                   ║
║       High-Performance Native AOT Actor System           ║
╚══════════════════════════════════════════════════════════╝

🏭 Silo ID: silo-xxxxxx
🔌 Redis:   localhost:6379
⚡ Native AOT: Enabled
🚀 Started at: 2026-01-31 12:30:00 UTC

✅ Silo is ready to host actors
📋 Actor types registered: Order, Driver, Chef, Kitchen, Inventory, Restaurant
```

### 4. Start the Gateway API

Open another terminal and start the Gateway:

```bash
cd productExample/src/Quark.AwesomePizza.Gateway
dotnet run
```

You should see:

```
╔══════════════════════════════════════╗
║  Awesome Pizza - Gateway API         ║
║  REST API + Real-time SSE            ║
╚══════════════════════════════════════╝

Gateway API starting on: http://localhost:5000
```

### 5. Start the MQTT Bridge (Optional - for IoT Integration)

Open a third terminal and start the MQTT Bridge:

```bash
cd productExample/src/Quark.AwesomePizza.MqttBridge
dotnet run
```

You should see:

```
╔══════════════════════════════════════════════════════════╗
║       Awesome Pizza - MQTT Bridge Service               ║
║       Real-time IoT Integration with MQTTnet            ║
╚══════════════════════════════════════════════════════════╝

🔌 MQTT Broker: localhost:1883
✅ Connected to MQTT broker
✅ Subscribed to all topics
```

### 6. Test the API

#### Create an Order

```bash
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "customer-123",
    "restaurantId": "restaurant-1",
    "items": [
      {
        "pizzaType": "Margherita",
        "size": "Large",
        "toppings": ["cheese", "tomato", "basil"],
        "quantity": 2,
        "price": 15.99
      }
    ],
    "deliveryAddress": {
      "latitude": 40.7128,
      "longitude": -74.0060,
      "timestamp": "2026-01-31T12:00:00Z"
    },
    "specialInstructions": "Extra cheese please"
  }'
```

Response:

```json
{
  "orderId": "order-xxxxx",
  "state": {
    "orderId": "order-xxxxx",
    "customerId": "customer-123",
    "restaurantId": "restaurant-1",
    "status": "Created",
    "createdAt": "2026-01-31T12:00:00Z",
    "totalAmount": 31.98,
    ...
  },
  "estimatedDeliveryTime": "2026-01-31T12:45:00Z"
}
```

#### Get Order Status

```bash
curl http://localhost:5000/api/orders/{orderId}
```

#### Confirm Order

```bash
curl -X POST http://localhost:5000/api/orders/{orderId}/confirm
```

#### Register a Driver

```bash
curl -X POST "http://localhost:5000/api/drivers?driverId=driver-1&name=John%20Doe"
```

#### Assign Driver to Order

```bash
curl -X POST "http://localhost:5000/api/orders/{orderId}/assign-driver?driverId=driver-1"
```

#### Update Driver Location

```bash
curl -X POST http://localhost:5000/api/drivers/driver-1/location \
  -H "Content-Type: application/json" \
  -d '{
    "driverId": "driver-1",
    "latitude": 40.7580,
    "longitude": -73.9855,
    "timestamp": "2026-01-31T12:15:00Z"
  }'
```

#### Real-time Order Tracking (SSE)

```bash
curl -N http://localhost:5000/api/orders/{orderId}/track
```

This will open a Server-Sent Events stream that sends real-time updates about the order.

### 7. Testing MQTT Integration (Optional)

If you started the MQTT Bridge, you can send driver location updates via MQTT:

```bash
# Install mosquitto clients (if not already installed)
# Ubuntu/Debian: sudo apt-get install mosquitto-clients
# macOS: brew install mosquitto

# Send driver location update
mosquitto_pub -t "pizza/drivers/driver-1/location" \
  -m '{"lat":40.7128,"lon":-74.0060,"timestamp":"2026-01-31T12:00:00Z"}'

# Send driver status update
mosquitto_pub -t "pizza/drivers/driver-1/status" \
  -m '{"status":"Available"}'

# Verify the update via Gateway API
curl http://localhost:5000/api/drivers/driver-1
```

The MQTT Bridge will automatically:
1. Receive the MQTT message
2. Create/update the DriverActor
3. Route location updates to the appropriate actor

See [MqttBridge README](src/Quark.AwesomePizza.MqttBridge/README.md) for more details.

### 8. Using the Silo Interactive Console

The Silo provides an interactive console for testing actors directly:

```
> create-order order-001 customer-123 restaurant-1
✅ Order created: order-001
   Customer: customer-123
   Restaurant: restaurant-1
   Status: Created
   Total: $12.99
   ETA: 12:45:00

> create-driver driver-001 "Alice Smith"
✅ Driver created: driver-001
   Name: Alice Smith
   Status: Available

> create-chef chef-001 "Bob Johnson"
✅ Chef created: chef-001
   Name: Bob Johnson
   Status: Available

> status order-001 Confirmed
✅ Order order-001 updated to: Confirmed
   Updated at: 12:05:00

> list
📋 Active actors on this silo: 3
   • OrderActor: order-001
   • DriverActor: driver-001
   • ChefActor: chef-001

> exit
```

## Available Endpoints

### Order Management

- `POST /api/orders` - Create new order
- `GET /api/orders/{orderId}` - Get order details
- `POST /api/orders/{orderId}/confirm` - Confirm order
- `POST /api/orders/{orderId}/assign-driver` - Assign driver
- `POST /api/orders/{orderId}/start-delivery` - Start delivery
- `POST /api/orders/{orderId}/complete-delivery` - Mark as delivered
- `POST /api/orders/{orderId}/cancel` - Cancel order
- `GET /api/orders/{orderId}/track` - Real-time tracking (SSE)

### Driver Management

- `POST /api/drivers` - Register new driver
- `GET /api/drivers/{driverId}` - Get driver status
- `POST /api/drivers/{driverId}/location` - Update location
- `POST /api/drivers/{driverId}/status` - Update status

### System Health

- `GET /health` - Health check
- `GET /` - API information

## Order Lifecycle

1. **Created** - Order placed by customer
2. **Confirmed** - Order confirmed, sent to kitchen
3. **Preparing** - Chef assigned, preparing ingredients
4. **Baking** - Pizza in the oven
5. **Ready** - Pizza ready for pickup
6. **DriverAssigned** - Driver assigned to deliver
7. **OutForDelivery** - Driver picked up order
8. **Delivered** - Order delivered to customer

## Architecture

```
Customer App
     ↓
Gateway API (Port 5000)
     ↓
Silo (Actor Host)
  ├── OrderActor
  ├── DriverActor
  ├── ChefActor
  ├── KitchenActor
  ├── InventoryActor
  └── RestaurantActor
     ↓
Redis (Port 6379)
```

## Troubleshooting

### Redis Connection Issues

If you see Redis connection errors:

```bash
# Check if Redis is running
docker ps | grep redis

# Restart Redis
docker-compose restart redis

# View Redis logs
docker logs awesomepizza-redis
```

### Port Already in Use

If port 5000 or 6379 is already in use:

```bash
# For Gateway (port 5000)
# Edit appsettings.json and change "Urls": "http://localhost:5001"

# For Redis (port 6379)
# Edit docker-compose.yml and change ports: "6380:6379"
# Then update appsettings.json: "Redis": "localhost:6380"
```

### Source Generator Not Working

If you get "No factory registered for actor type" errors:

```bash
# Clean and rebuild
dotnet clean
dotnet build
```

## Next Steps

1. **Implement MQTT Bridge** - For real-time driver GPS updates
2. **Add Tests** - Unit and integration tests
3. **Add UI** - React/Vue/Svelte frontend
4. **Enable AOT** - Publish with Native AOT for production
5. **Add Monitoring** - Prometheus metrics and Grafana dashboards

## Production Deployment

To build for production with Native AOT:

```bash
cd productExample/src/Quark.AwesomePizza.Silo
dotnet publish -c Release -r linux-x64 -p:PublishAot=true

# Binary will be in: bin/Release/net10.0/linux-x64/publish/
# Startup time: ~50ms (vs ~500ms with JIT)
# Memory usage: <50MB per silo
```

## Learn More

- [Full Implementation Plan](plans/01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md)
- [Feature Specifications](plans/02-FEATURE-SPECIFICATIONS.md)
- [Architecture Overview](plans/04-ARCHITECTURE-OVERVIEW.md)
- [Quark Framework Docs](../docs/)

## Support

For issues or questions:

1. Check the planning documents in `productExample/plans/`
2. Review the [Quick Start Guide](plans/03-QUICK-START-GUIDE.md)
3. Open an issue on GitHub

---

**Happy Coding! 🍕🚀**
