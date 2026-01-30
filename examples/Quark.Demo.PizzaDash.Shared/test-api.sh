#!/bin/bash

# Quark Pizza Dash - API Test Script
# Demonstrates the full order lifecycle

API_URL="http://localhost:5000"

echo "╔══════════════════════════════════════════════════════════╗"
echo "║       Quark Pizza Dash - API Demo Script                ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# Check if API is running
echo "🔍 Checking API health..."
HEALTH=$(curl -s "${API_URL}/health" 2>&1)
if [ $? -ne 0 ]; then
    echo "❌ API is not running. Please start it first:"
    echo "   cd examples/Quark.Demo.PizzaDash.Api"
    echo "   dotnet run --urls http://localhost:5000"
    exit 1
fi

echo "✅ API is healthy"
echo "$HEALTH" | jq '.' 2>/dev/null || echo "$HEALTH"
echo ""

# Create an order
echo "📦 Creating a new pizza order..."
ORDER_RESPONSE=$(curl -s -X POST "${API_URL}/api/orders" \
  -H "Content-Type: application/json" \
  -d '{"customerId": "customer-demo-001", "pizzaType": "Pepperoni with Extra Cheese"}')

ORDER_ID=$(echo "$ORDER_RESPONSE" | jq -r '.orderId' 2>/dev/null)

if [ -z "$ORDER_ID" ] || [ "$ORDER_ID" = "null" ]; then
    echo "❌ Failed to create order"
    echo "$ORDER_RESPONSE"
    exit 1
fi

echo "✅ Order created successfully!"
echo "$ORDER_RESPONSE" | jq '.' 2>/dev/null || echo "$ORDER_RESPONSE"
echo ""
echo "📋 Order ID: $ORDER_ID"
echo ""

# Wait a moment
sleep 1

# Get order status
echo "🔍 Fetching order status..."
curl -s "${API_URL}/api/orders/${ORDER_ID}" | jq '.' 2>/dev/null
echo ""

# Update to PreparingDough
echo "👨‍🍳 Updating status to PreparingDough..."
sleep 1
curl -s -X PUT "${API_URL}/api/orders/${ORDER_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"newStatus": "PreparingDough"}' | jq '.' 2>/dev/null
echo ""

# Update to Baking
echo "🔥 Updating status to Baking..."
sleep 1
curl -s -X PUT "${API_URL}/api/orders/${ORDER_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"newStatus": "Baking"}' | jq '.' 2>/dev/null
echo ""

# Update to ReadyForPickup
echo "✅ Updating status to ReadyForPickup..."
sleep 1
curl -s -X PUT "${API_URL}/api/orders/${ORDER_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"newStatus": "ReadyForPickup"}' | jq '.' 2>/dev/null
echo ""

# Assign a driver
echo "🚗 Assigning driver..."
sleep 1
curl -s -X POST "${API_URL}/api/orders/${ORDER_ID}/driver?driverId=driver-101" | jq '.' 2>/dev/null
echo ""

# Update driver location
echo "📍 Updating driver location..."
sleep 1
curl -s -X PUT "${API_URL}/api/drivers/driver-101/location" \
  -H "Content-Type: application/json" \
  -d '{"latitude": 37.7749, "longitude": -122.4194, "timestamp": "'$(date -u +%Y-%m-%dT%H:%M:%SZ)'"}' | jq '.' 2>/dev/null
echo ""

# Update to OutForDelivery
echo "🚚 Updating status to OutForDelivery..."
sleep 1
curl -s -X PUT "${API_URL}/api/orders/${ORDER_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"newStatus": "OutForDelivery"}' | jq '.' 2>/dev/null
echo ""

# Final status - Delivered
echo "🎉 Updating status to Delivered..."
sleep 1
curl -s -X PUT "${API_URL}/api/orders/${ORDER_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"newStatus": "Delivered"}' | jq '.' 2>/dev/null
echo ""

# Get final health check
echo "🔍 Final health check..."
curl -s "${API_URL}/health" | jq '.' 2>/dev/null
echo ""

echo "╔══════════════════════════════════════════════════════════╗"
echo "║       Demo Complete!                                     ║"
echo "║       Order: ${ORDER_ID:0:36}           ║"
echo "║       Status: Delivered ✅                               ║"
echo "╚══════════════════════════════════════════════════════════╝"
