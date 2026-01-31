# MQTT Bridge - Usage Guide

## Overview

The MQTT Bridge service connects IoT devices (driver GPS devices, kitchen equipment) to the Quark actor system via MQTT protocol. It uses the MQTTnet library for robust MQTT communication with automatic reconnection.

## Features

- ✅ **Automatic Reconnection** - Handles network interruptions gracefully
- ✅ **Multiple Topic Subscriptions** - Listens to driver location, status, kitchen telemetry
- ✅ **Actor Integration** - Routes MQTT messages directly to Quark actors
- ✅ **Flexible Message Formats** - Supports various JSON payload formats
- ✅ **Command-line Configuration** - Easy to customize broker settings

## Prerequisites

- MQTT Broker running (Mosquitto recommended)
- Redis running (for actor state)
- .NET 10 SDK

## Quick Start

### 1. Start Infrastructure

```bash
# Start Redis and MQTT broker with Docker Compose
cd productExample
docker-compose up -d

# Verify services are running
docker-compose ps
```

### 2. Run the MQTT Bridge

```bash
cd src/Quark.AwesomePizza.MqttBridge
dotnet run

# Or with custom broker settings
dotnet run -- --host 192.168.1.100 --port 1883
```

Expected output:
```
╔══════════════════════════════════════════════════════════╗
║       Awesome Pizza - MQTT Bridge Service               ║
║       Real-time IoT Integration with MQTTnet            ║
╚══════════════════════════════════════════════════════════╝

🔌 MQTT Broker: localhost:1883
🆔 Client ID: awesomepizza-bridge-xxxxx
🚀 Started at: 2026-01-31 12:00:00 UTC

✅ Actor factory initialized
⏳ Connecting to MQTT broker...
✅ Connected to MQTT broker
✅ Subscribed to all topics

📋 Subscribed Topics:
   • pizza/drivers/+/location    - Driver GPS updates
   • pizza/drivers/+/status      - Driver status changes
   • pizza/kitchen/+/oven        - Oven telemetry
   • pizza/kitchen/+/alerts      - Equipment alerts
   • pizza/orders/+/events       - Order events
```

### 3. Test with MQTT Messages

Open another terminal and publish test messages:

#### Driver Location Update

```bash
# Basic format
mosquitto_pub -t "pizza/drivers/driver-1/location" \
  -m '{"lat":40.7128,"lon":-74.0060,"timestamp":"2026-01-31T12:00:00Z"}'

# Alternative format (latitude/longitude)
mosquitto_pub -t "pizza/drivers/driver-1/location" \
  -m '{"latitude":40.7128,"longitude":-74.0060}'
```

Bridge output:
```
📩 Message received on topic: pizza/drivers/driver-1/location
   🆕 Created new actor: DriverActor (driver-1)
   ✅ Updated location for driver driver-1: (40.7128, -74.006)
```

#### Driver Status Update

```bash
mosquitto_pub -t "pizza/drivers/driver-1/status" \
  -m '{"status":"Available"}'

mosquitto_pub -t "pizza/drivers/driver-1/status" \
  -m '{"status":"Busy"}'
```

Bridge output:
```
📩 Message received on topic: pizza/drivers/driver-1/status
   ✅ Updated status for driver driver-1: Available
```

#### Kitchen Oven Telemetry

```bash
mosquitto_pub -t "pizza/kitchen/kitchen-1/oven" \
  -m '{"temperature":450,"timer":12}'
```

Bridge output:
```
📩 Message received on topic: pizza/kitchen/kitchen-1/oven
   📝 Kitchen telemetry for kitchen-1: oven
   Data: {"temperature":450,"timer":12}
```

## MQTT Topics

### Driver Topics

| Topic | Purpose | Example Payload |
|-------|---------|-----------------|
| `pizza/drivers/{driverId}/location` | GPS coordinates | `{"lat":40.7128,"lon":-74.0060}` |
| `pizza/drivers/{driverId}/status` | Driver availability | `{"status":"Available"}` |

### Kitchen Topics

| Topic | Purpose | Example Payload |
|-------|---------|-----------------|
| `pizza/kitchen/{kitchenId}/oven` | Oven telemetry | `{"temperature":450,"timer":12}` |
| `pizza/kitchen/{kitchenId}/alerts` | Equipment alerts | `{"alert":"overheat","severity":"high"}` |

### Order Topics

| Topic | Purpose | Example Payload |
|-------|---------|-----------------|
| `pizza/orders/{orderId}/events` | Order events | `{"event":"status_changed","status":"Delivered"}` |

## Message Formats

### Driver Location

Supports both field name formats:

```json
{
  "lat": 40.7128,
  "lon": -74.0060,
  "timestamp": "2026-01-31T12:00:00Z"
}
```

Or:

```json
{
  "latitude": 40.7128,
  "longitude": -74.0060,
  "timestamp": "2026-01-31T12:00:00Z"
}
```

### Driver Status

Valid status values: `Available`, `Busy`, `OnBreak`, `Offline`

```json
{
  "status": "Available"
}
```

## Command-Line Options

```bash
dotnet run -- [options]

Options:
  --host <hostname>     MQTT broker hostname (default: localhost)
  --port <port>         MQTT broker port (default: 1883)
  --client-id <id>      MQTT client ID (default: auto-generated)
```

## Configuration

Edit `appsettings.json` to customize settings:

```json
{
  "Mqtt": {
    "BrokerHost": "localhost",
    "BrokerPort": 1883,
    "ClientId": "awesomepizza-bridge",
    "ReconnectDelay": 5,
    "KeepAlive": 60
  }
}
```

## Integration with Gateway

The MQTT bridge runs independently and communicates with actors directly. For end-to-end testing:

1. **Terminal 1**: Start Silo
   ```bash
   cd src/Quark.AwesomePizza.Silo
   dotnet run
   ```

2. **Terminal 2**: Start Gateway
   ```bash
   cd src/Quark.AwesomePizza.Gateway
   dotnet run
   ```

3. **Terminal 3**: Start MQTT Bridge
   ```bash
   cd src/Quark.AwesomePizza.MqttBridge
   dotnet run
   ```

4. **Terminal 4**: Send MQTT messages
   ```bash
   mosquitto_pub -t "pizza/drivers/driver-1/location" \
     -m '{"lat":40.7128,"lon":-74.0060}'
   ```

5. **Terminal 5**: Check via Gateway API
   ```bash
   curl http://localhost:5000/api/drivers/driver-1
   ```

## Simulating Driver GPS with MQTT

Create a simple GPS simulator script:

```bash
#!/bin/bash
# simulate-driver.sh

DRIVER_ID="driver-1"
LAT=40.7128
LON=-74.0060

while true; do
  # Simulate movement
  LAT=$(echo "$LAT + 0.001" | bc)
  LON=$(echo "$LON - 0.001" | bc)
  
  # Publish location
  mosquitto_pub -t "pizza/drivers/$DRIVER_ID/location" \
    -m "{\"lat\":$LAT,\"lon\":$LON,\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"
  
  echo "Published location: $LAT, $LON"
  sleep 5
done
```

Run it:
```bash
chmod +x simulate-driver.sh
./simulate-driver.sh
```

## Troubleshooting

### Connection Issues

**Problem**: `❌ Connection failed: Connection refused`

**Solution**:
1. Verify MQTT broker is running: `docker ps | grep mosquitto`
2. Check broker is on correct port: `netstat -an | grep 1883`
3. Test with mosquitto_sub: `mosquitto_sub -t "test" -v`

### Message Not Processed

**Problem**: Messages received but not processed

**Solution**:
1. Check JSON format is valid
2. Verify topic format matches pattern
3. Check bridge logs for parsing errors
4. Ensure driver/actor IDs are valid

### Actor Not Created

**Problem**: `❌ Failed to get/create driver actor`

**Solution**:
1. Verify Shared project is referenced correctly
2. Check actor factory is initialized
3. Ensure no exceptions during actor creation

## Advanced Usage

### Custom Message Handlers

To add custom message handling, modify `HandleDriverMessageAsync` or add new handlers:

```csharp
private static async Task HandleCustomTopicAsync(string topic, string payload)
{
    // Your custom logic here
    var data = JsonSerializer.Deserialize<CustomMessage>(payload);
    // Process and route to actors
}
```

### Multiple Broker Support

To connect to multiple MQTT brokers, create multiple `IManagedMqttClient` instances with different configurations.

### TLS/SSL Support

For production, enable TLS:

```csharp
.WithTls(new MqttClientTlsOptions
{
    UseTls = true,
    CertificateValidationHandler = _ => true // For testing only!
})
```

## Performance

- **Connection**: Auto-reconnects every 5 seconds on failure
- **QoS Level**: At Least Once (QoS 1) for reliable delivery
- **Keep-Alive**: 60 seconds
- **Message Processing**: Async/await for non-blocking I/O

## Security Considerations

**For Production**:

1. Enable authentication in Mosquitto:
   ```conf
   allow_anonymous false
   password_file /mosquitto/config/passwords
   ```

2. Use TLS for encrypted communication
3. Implement per-device access control lists (ACLs)
4. Rotate credentials regularly
5. Monitor for anomalous traffic patterns

## Next Steps

- [ ] Add authentication to MQTT broker
- [ ] Implement kitchen telemetry actor handlers
- [ ] Add order event processing
- [ ] Create UI dashboard for MQTT monitoring
- [ ] Add metrics and monitoring (Prometheus)

## Learn More

- [MQTTnet Documentation](https://github.com/dotnet/MQTTnet)
- [MQTT Protocol Specification](https://mqtt.org/mqtt-specification/)
- [Mosquitto Configuration](https://mosquitto.org/man/mosquitto-conf-5.html)

---

**Version**: 1.0.0  
**Last Updated**: 2026-01-31
