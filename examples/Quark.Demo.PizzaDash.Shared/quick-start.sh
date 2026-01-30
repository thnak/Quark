#!/bin/bash

# Quark Pizza Dash - Quick Start Script
# Runs all components locally for testing

echo "╔══════════════════════════════════════════════════════════╗"
echo "║       Quark Pizza Dash - Quick Start                    ║"
echo "║       Starting all components locally...                ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# Change to the examples directory
cd "$(dirname "$0")"

# Check if dotnet is available
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Please install .NET 10 SDK."
    exit 1
fi

echo "✅ .NET SDK found"
echo ""

# Build all projects
echo "🔨 Building all projects..."
cd ../../
dotnet build examples/Quark.Demo.PizzaDash.Shared/Quark.Demo.PizzaDash.Shared.csproj -m
dotnet build examples/Quark.Demo.PizzaDash.Silo/Quark.Demo.PizzaDash.Silo.csproj -m
dotnet build examples/Quark.Demo.PizzaDash.Api/Quark.Demo.PizzaDash.Api.csproj -m
dotnet build examples/Quark.Demo.PizzaDash.KitchenDisplay/Quark.Demo.PizzaDash.KitchenDisplay.csproj -m

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo ""
echo "✅ Build succeeded"
echo ""
echo "🚀 Starting components..."
echo ""
echo "You can now interact with the system:"
echo "  - Kitchen Silo: Run 'dotnet run --project examples/Quark.Demo.PizzaDash.Silo'"
echo "  - Customer API: Run 'dotnet run --project examples/Quark.Demo.PizzaDash.Api --urls http://localhost:5000'"
echo "  - Kitchen Display: Run 'dotnet run --project examples/Quark.Demo.PizzaDash.KitchenDisplay'"
echo ""
echo "API Endpoints (when API is running):"
echo "  - Health: curl http://localhost:5000/health"
echo "  - Create Order: curl -X POST http://localhost:5000/api/orders -H 'Content-Type: application/json' -d '{\"customerId\":\"cust-1\",\"pizzaType\":\"Margherita\"}'"
echo "  - Get Order: curl http://localhost:5000/api/orders/{orderId}"
echo ""
echo "💡 Tip: Run each component in a separate terminal window"
