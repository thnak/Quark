#!/bin/bash
# Test script for MQTT Bridge
# Requires mosquitto_pub to be installed

set -e

echo "╔══════════════════════════════════════════════════════════╗"
echo "║       MQTT Bridge Test Script                            ║"
echo "║       Simulates IoT Device Messages                      ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# Check if mosquitto_pub is installed
if ! command -v mosquitto_pub &> /dev/null; then
    echo "❌ mosquitto_pub not found!"
    echo "   Install: sudo apt-get install mosquitto-clients (Ubuntu/Debian)"
    echo "   Install: brew install mosquitto (macOS)"
    exit 1
fi

MQTT_HOST="${MQTT_HOST:-localhost}"
MQTT_PORT="${MQTT_PORT:-1883}"

echo "🔌 MQTT Broker: $MQTT_HOST:$MQTT_PORT"
echo ""

# Test 1: Driver Location Update
echo "📍 Test 1: Driver Location Update"
mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
  -t "pizza/drivers/driver-test-1/location" \
  -m '{"lat":40.7128,"lon":-74.0060,"timestamp":"2026-01-31T12:00:00Z"}'
echo "   ✅ Sent location for driver-test-1"
sleep 1

# Test 2: Driver Status Update
echo ""
echo "📊 Test 2: Driver Status Update"
mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
  -t "pizza/drivers/driver-test-1/status" \
  -m '{"status":"Available"}'
echo "   ✅ Sent status update for driver-test-1"
sleep 1

# Test 3: Multiple Location Updates (Simulating Movement)
echo ""
echo "🚗 Test 3: Simulating Driver Movement"
locations=(
    '{"lat":40.7128,"lon":-74.0060}'
    '{"lat":40.7138,"lon":-74.0050}'
    '{"lat":40.7148,"lon":-74.0040}'
    '{"lat":40.7158,"lon":-74.0030}'
)

for i in "${!locations[@]}"; do
    mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
      -t "pizza/drivers/driver-test-2/location" \
      -m "${locations[$i]}"
    echo "   ✅ Update $((i+1))/4: ${locations[$i]}"
    sleep 0.5
done

# Test 4: Multiple Drivers
echo ""
echo "👥 Test 4: Multiple Drivers"
for driver_id in driver-1 driver-2 driver-3; do
    lat=$(echo "40.7128 + $RANDOM % 100 * 0.001" | bc)
    lon=$(echo "-74.0060 - $RANDOM % 100 * 0.001" | bc)
    
    mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
      -t "pizza/drivers/$driver_id/location" \
      -m "{\"lat\":$lat,\"lon\":$lon}"
    echo "   ✅ Sent location for $driver_id"
    sleep 0.3
done

# Test 5: Driver Status Changes
echo ""
echo "🔄 Test 5: Driver Status Changes"
statuses=("Available" "Busy" "OnBreak" "Available")
for status in "${statuses[@]}"; do
    mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
      -t "pizza/drivers/driver-test-3/status" \
      -m "{\"status\":\"$status\"}"
    echo "   ✅ Status changed to: $status"
    sleep 0.5
done

# Test 6: Kitchen Telemetry (Oven)
echo ""
echo "🔥 Test 6: Kitchen Oven Telemetry"
mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
  -t "pizza/kitchen/kitchen-1/oven" \
  -m '{"temperature":450,"timer":12,"status":"baking"}'
echo "   ✅ Sent oven telemetry"
sleep 1

# Test 7: Kitchen Alert
echo ""
echo "⚠️  Test 7: Kitchen Equipment Alert"
mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
  -t "pizza/kitchen/kitchen-1/alerts" \
  -m '{"alert":"high_temperature","severity":"warning","temperature":500}'
echo "   ✅ Sent equipment alert"
sleep 1

# Test 8: Order Event
echo ""
echo "📦 Test 8: Order Event"
mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" \
  -t "pizza/orders/order-123/events" \
  -m '{"event":"status_changed","status":"OutForDelivery","timestamp":"2026-01-31T12:30:00Z"}'
echo "   ✅ Sent order event"

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║       All Tests Completed Successfully! ✅               ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "💡 Check the MQTT Bridge console output to verify message processing"
echo ""
