using Quark.Core.Actors;
using Quark.Examples.PizzaTracker.Shared.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Console;

/// <summary>
/// Console application that simulates pizza tracking using Quark actors.
/// Demonstrates AOT-compatible actor usage for pizza delivery tracking.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║        Pizza GPS Tracker - Console Silo Example           ║");
        System.Console.WriteLine("║              Quark Framework AOT Showcase                  ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Create actor factory
        var factory = new ActorFactory();
        System.Console.WriteLine("✓ Actor factory created");

        // Create a pizza order
        var pizzaActor = factory.CreateActor<PizzaActor>("pizza-order-001");
        await pizzaActor.OnActivateAsync();
        System.Console.WriteLine($"✓ Pizza actor activated: {pizzaActor.ActorId}");

        // Subscribe to pizza status updates
        pizzaActor.Subscribe(update =>
        {
            System.Console.WriteLine($"  📬 Status Update: Order {update.OrderId} - {update.Status}");
            if (update.DriverLocation != null)
            {
                System.Console.WriteLine($"     📍 Driver Location: ({update.DriverLocation.Latitude:F6}, {update.DriverLocation.Longitude:F6})");
            }
        });

        // Create the order
        System.Console.WriteLine();
        System.Console.WriteLine("Creating pizza order...");
        var order = await pizzaActor.CreateOrderAsync("customer-123", "Pepperoni");
        System.Console.WriteLine($"✓ Order created: {order.OrderId}");
        System.Console.WriteLine($"  Pizza Type: {order.PizzaType}");
        System.Console.WriteLine($"  Customer: {order.CustomerId}");
        System.Console.WriteLine($"  Order Time: {order.OrderTime:yyyy-MM-dd HH:mm:ss}");

        // Simulate pizza preparation workflow
        System.Console.WriteLine();
        System.Console.WriteLine("Simulating pizza preparation workflow...");
        await Task.Delay(1000);

        await pizzaActor.UpdateStatusAsync(PizzaStatus.Preparing);
        await Task.Delay(2000);

        await pizzaActor.UpdateStatusAsync(PizzaStatus.Baking);
        await Task.Delay(3000);

        // Create a delivery driver
        var driverActor = factory.CreateActor<DeliveryDriverActor>("driver-001");
        await driverActor.OnActivateAsync();
        System.Console.WriteLine($"✓ Driver actor activated: {driverActor.ActorId}");

        // Assign driver to order
        await driverActor.AssignOrderAsync(pizzaActor.ActorId);
        await pizzaActor.UpdateStatusAsync(PizzaStatus.OutForDelivery, driverActor.ActorId);

        // Simulate driver movement with GPS updates
        System.Console.WriteLine();
        System.Console.WriteLine("Simulating driver GPS tracking...");
        var locations = new[]
        {
            (40.7128, -74.0060),  // New York starting point
            (40.7138, -74.0050),
            (40.7148, -74.0040),
            (40.7158, -74.0030),
            (40.7168, -74.0020),  // Destination
        };

        foreach (var (lat, lon) in locations)
        {
            await Task.Delay(1500);
            await driverActor.UpdateLocationAsync(lat, lon);
            var location = await driverActor.GetLocationAsync();
            if (location != null)
            {
                await pizzaActor.UpdateDriverLocationAsync(location);
            }
        }

        // Mark as delivered
        await Task.Delay(1000);
        await pizzaActor.UpdateStatusAsync(PizzaStatus.Delivered);
        await driverActor.CompleteDeliveryAsync();

        // Display final order status
        System.Console.WriteLine();
        System.Console.WriteLine("═══════════════════════════════════════════════════════════");
        var finalOrder = await pizzaActor.GetOrderAsync();
        if (finalOrder != null)
        {
            System.Console.WriteLine("Final Order Status:");
            System.Console.WriteLine($"  Order ID: {finalOrder.OrderId}");
            System.Console.WriteLine($"  Status: {finalOrder.Status}");
            System.Console.WriteLine($"  Pizza Type: {finalOrder.PizzaType}");
            System.Console.WriteLine($"  Driver: {finalOrder.DriverId ?? "N/A"}");
            if (finalOrder.DriverLocation != null)
            {
                System.Console.WriteLine($"  Final Location: ({finalOrder.DriverLocation.Latitude:F6}, {finalOrder.DriverLocation.Longitude:F6})");
            }
        }
        System.Console.WriteLine("═══════════════════════════════════════════════════════════");

        // Cleanup
        await pizzaActor.OnDeactivateAsync();
        await driverActor.OnDeactivateAsync();

        System.Console.WriteLine();
        System.Console.WriteLine("✓ Pizza tracking simulation completed successfully!");
        System.Console.WriteLine();
    }
}
