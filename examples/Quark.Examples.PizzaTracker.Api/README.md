# Pizza GPS Tracker - Quark Framework Example

This example demonstrates the Quark actor framework's capabilities with a real-world pizza delivery tracking application. It showcases:

- **AOT (Ahead-of-Time) Compilation**: Full Native AOT support for both console and API applications
- **Distributed Actor System**: Multiple silos with Redis-based clustering
- **Server-Sent Events (SSE)**: Real-time pizza tracking using FastEndpoints
- **Docker Support**: Easy deployment with docker-compose

## Architecture

The example consists of three projects:

1. **Quark.Examples.PizzaTracker.Shared**: Common models and actors
   - `PizzaActor`: Manages pizza order state and lifecycle
   - `DeliveryDriverActor`: Tracks driver GPS location
   - Models: `PizzaOrder`, `PizzaStatus`, `GpsLocation`, etc.

2. **Quark.Examples.PizzaTracker.Console**: Console application demonstrating basic actor usage
   - Creates and tracks a pizza order
   - Simulates driver GPS movement
   - Shows actor activation and state management

3. **Quark.Examples.PizzaTracker.Api**: Minimal API with FastEndpoints
   - RESTful endpoints for order management
   - Server-Sent Events for real-time tracking
   - Multiple API silos for load distribution

## Prerequisites

- .NET 10 SDK
- Docker and Docker Compose (for multi-silo deployment)

## Running the Console Application

```bash
# From the repository root
cd examples/Quark.Examples.PizzaTracker.Console
dotnet run
```

This will simulate a complete pizza order workflow:
1. Create a pizza order
2. Progress through preparation stages
3. Assign a delivery driver
4. Track GPS updates as the driver moves
5. Mark as delivered

## Running the API Application

```bash
# From the repository root
cd examples/Quark.Examples.PizzaTracker.Api
dotnet run
```

The API will start on `http://localhost:5000` with the following endpoints:

### API Endpoints

#### Create Order
```bash
POST http://localhost:5000/api/orders
Content-Type: application/json

{
  "customerId": "customer-123",
  "pizzaType": "Pepperoni"
}
```

Response:
```json
{
  "orderId": "order-abc123...",
  "status": "Ordered",
  "orderTime": "2026-01-29T10:00:00Z"
}
```

#### Update Order Status
```bash
PUT http://localhost:5000/api/orders/{orderId}/status
Content-Type: application/json

{
  "status": "Preparing",
  "driverId": null
}
```

#### Update Driver Location
```bash
PUT http://localhost:5000/api/drivers/{driverId}/location
Content-Type: application/json

{
  "latitude": 40.7128,
  "longitude": -74.0060
}
```

#### Track Order (Server-Sent Events)
```bash
GET http://localhost:5000/api/orders/{orderId}/track
```

This endpoint returns a stream of Server-Sent Events. You can test it with:

```bash
# Using curl
curl -N http://localhost:5000/api/orders/{orderId}/track

# Using EventSource in JavaScript
const eventSource = new EventSource('http://localhost:5000/api/orders/{orderId}/track');
eventSource.addEventListener('status', (event) => {
  const update = JSON.parse(event.data);
  console.log('Pizza Status:', update);
});
```

Example SSE output:
```
event: status
data: {"orderId":"order-123","status":"Ordered","timestamp":"2026-01-29T10:00:00Z"}

event: status
data: {"orderId":"order-123","status":"Preparing","timestamp":"2026-01-29T10:05:00Z"}

event: status
data: {"orderId":"order-123","status":"OutForDelivery","timestamp":"2026-01-29T10:20:00Z","driverLocation":{"latitude":40.7128,"longitude":-74.0060,"timestamp":"2026-01-29T10:20:00Z"}}
```

## Running with Docker Compose (Multi-Silo Cluster)

The docker-compose setup demonstrates true distributed deployment with:
- 3 API silos for load balancing
- 1 console silo for background processing
- Redis for cluster membership and state

```bash
# From the examples/Quark.Examples.PizzaTracker.Api directory
docker-compose up --build
```

This will start:
- **Redis**: Cluster membership and state storage (port 6379)
- **api-silo-1**: API instance (port 5001)
- **api-silo-2**: API instance (port 5002)
- **api-silo-3**: API instance (port 5003)
- **console-silo**: Background worker silo

You can then access any API silo:
```bash
# Create order on silo 1
curl -X POST http://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"customer-123","pizzaType":"Margherita"}'

# Track on silo 2 (actors are location-transparent via consistent hashing)
curl -N http://localhost:5002/api/orders/{orderId}/track
```

## AOT Publishing

Both projects are configured for Native AOT compilation:

```bash
# Publish Console App
cd examples/Quark.Examples.PizzaTracker.Console
dotnet publish -c Release -r linux-x64 --self-contained

# Publish API
cd examples/Quark.Examples.PizzaTracker.Api
dotnet publish -c Release -r linux-x64 --self-contained
```

The resulting binaries:
- Are self-contained (no .NET runtime required)
- Have fast startup times
- Have minimal memory footprint
- Are fully AOT-compiled with no reflection

## Testing the Complete Workflow

1. Start the API:
   ```bash
   cd examples/Quark.Examples.PizzaTracker.Api
   dotnet run
   ```

2. In another terminal, create an order:
   ```bash
   curl -X POST http://localhost:5000/api/orders \
     -H "Content-Type: application/json" \
     -d '{"customerId":"customer-123","pizzaType":"Pepperoni"}' \
     | jq .
   ```
   Note the `orderId` from the response.

3. Start tracking in another terminal:
   ```bash
   curl -N http://localhost:5000/api/orders/{orderId}/track
   ```

4. Update the order status:
   ```bash
   curl -X PUT http://localhost:5000/api/orders/{orderId}/status \
     -H "Content-Type: application/json" \
     -d '{"status":"Preparing"}'
   
   curl -X PUT http://localhost:5000/api/orders/{orderId}/status \
     -H "Content-Type: application/json" \
     -d '{"status":"Baking"}'
   
   curl -X PUT http://localhost:5000/api/orders/{orderId}/status \
     -H "Content-Type: application/json" \
     -d '{"status":"OutForDelivery","driverId":"driver-001"}'
   ```

5. Simulate driver GPS updates:
   ```bash
   curl -X PUT http://localhost:5000/api/drivers/driver-001/location \
     -H "Content-Type: application/json" \
     -d '{"latitude":40.7128,"longitude":-74.0060}'
   
   curl -X PUT http://localhost:5000/api/drivers/driver-001/location \
     -H "Content-Type: application/json" \
     -d '{"latitude":40.7138,"longitude":-74.0050}'
   ```

6. Mark as delivered:
   ```bash
   curl -X PUT http://localhost:5000/api/orders/{orderId}/status \
     -H "Content-Type: application/json" \
     -d '{"status":"Delivered"}'
   ```

You should see all updates streaming in real-time to the tracking terminal!

## Key Features Demonstrated

### AOT Compatibility
- No reflection used anywhere in the code
- Source-generated JSON serialization (`PizzaTrackerJsonContext`)
- Source-generated actor factories (via Quark.Generators)
- FastEndpoints configured for AOT

### Actor Model
- **Location Transparency**: Actors can be accessed from any silo
- **State Management**: Each actor maintains its own state
- **Event Notifications**: Actors can notify subscribers of state changes

### Distributed System
- **Cluster Membership**: Redis-based silo discovery
- **Consistent Hashing**: Actors are distributed across silos
- **Horizontal Scaling**: Add more silos to handle increased load

### Real-Time Communication
- **Server-Sent Events**: Unidirectional server-to-client streaming
- **Low Latency**: Updates pushed immediately to subscribers
- **Connection Resilience**: Clients can reconnect and get current state

## Project Structure

```
Quark.Examples.PizzaTracker/
├── Shared/                          # Common library
│   ├── Models/
│   │   └── PizzaModels.cs          # Data models
│   └── Actors/
│       ├── PizzaActor.cs           # Pizza order actor
│       └── DeliveryDriverActor.cs  # Driver tracking actor
├── Console/                         # Console application
│   ├── Program.cs                  # Main entry point
│   ├── Dockerfile                  # Docker build config
│   └── Quark.Examples.PizzaTracker.Console.csproj
└── Api/                            # Web API
    ├── Program.cs                  # API startup
    ├── Endpoints/                  # FastEndpoints definitions
    │   ├── CreateOrderEndpoint.cs
    │   ├── UpdateStatusEndpoint.cs
    │   ├── UpdateDriverLocationEndpoint.cs
    │   ├── TrackPizzaEndpoint.cs
    │   └── RootEndpoint.cs
    ├── PizzaTrackerJsonContext.cs  # AOT JSON serialization
    ├── Dockerfile                  # Docker build config
    ├── docker-compose.yml          # Multi-silo orchestration
    └── Quark.Examples.PizzaTracker.Api.csproj
```

## Technologies Used

- **Quark Framework**: AOT-compatible actor framework
- **FastEndpoints**: High-performance minimal API framework with SSE support
- **Redis**: Cluster membership and distributed state
- **gRPC**: Inter-silo communication
- **Docker**: Container orchestration
- **.NET 10 Native AOT**: Ahead-of-time compilation

## License

MIT License - Same as Quark framework
