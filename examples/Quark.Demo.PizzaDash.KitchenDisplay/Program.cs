using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

namespace Quark.Demo.PizzaDash.KitchenDisplay;

/// <summary>
/// Kitchen Display - Consumes orders from the kitchen/new-orders stream.
/// Simulates a real-time display for chefs showing what to cook next.
/// </summary>
class Program
{
    private static readonly Queue<KitchenOrder> _orderQueue = new();
    private static readonly Dictionary<string, ChefActor> _chefs = new();
    private static readonly ActorFactory _factory = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Quark Pizza Dash - Kitchen Display                ║");
        Console.WriteLine("║       Real-time Order Stream Consumer                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("🔥 Subscribing to kitchen/new-orders stream...");
        Console.WriteLine("👨‍🍳 Initializing chef pool...");
        Console.WriteLine();

        // Initialize chef actors
        for (int i = 1; i <= 3; i++)
        {
            var chefId = $"chef-{i}";
            var chef = _factory.CreateActor<ChefActor>(chefId);
            await chef.OnActivateAsync();
            _chefs[chefId] = chef;
            Console.WriteLine($"✅ Chef {i} ready for orders");
        }

        Console.WriteLine();
        Console.WriteLine("📋 Kitchen Display Active - Waiting for orders...");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  order <pizzaType>      - Simulate new order");
        Console.WriteLine("  complete <orderId>     - Mark order complete");
        Console.WriteLine("  status                 - Show queue status");
        Console.WriteLine("  chefs                  - Show chef workload");
        Console.WriteLine("  exit                   - Shutdown display");
        Console.WriteLine();

        var cts = new CancellationTokenSource();
        
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        // Simulate stream consumer (in real system, this would subscribe to actual stream)
        var streamTask = Task.Run(() => StreamConsumerLoop(cts.Token), cts.Token);

        // Command loop
        await CommandLoop(cts.Token);

        await cts.CancelAsync();
        await streamTask;

        Console.WriteLine("👋 Kitchen Display shutdown complete");
    }

    private static async Task CommandLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            try
            {
                switch (parts[0].ToLower())
                {
                    case "order" when parts.Length >= 2:
                        await SimulateNewOrder(string.Join(" ", parts.Skip(1)));
                        break;

                    case "complete" when parts.Length >= 2:
                        await CompleteOrder(parts[1]);
                        break;

                    case "status":
                        ShowQueueStatus();
                        break;

                    case "chefs":
                        await ShowChefWorkload();
                        break;

                    case "exit":
                    case "quit":
                        return;

                    default:
                        Console.WriteLine("❌ Unknown command");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }
    }

    private static async Task SimulateNewOrder(string pizzaType)
    {
        var orderId = $"order-{Guid.NewGuid().ToString("N")[..8]}";
        var order = new KitchenOrder(orderId, pizzaType, DateTime.UtcNow);
        
        _orderQueue.Enqueue(order);
        
        Console.WriteLine($"🆕 New Order Received:");
        Console.WriteLine($"   Order ID: {orderId}");
        Console.WriteLine($"   Pizza: {pizzaType}");
        Console.WriteLine($"   Time: {order.OrderTime:HH:mm:ss}");

        // Assign to least busy chef
        var chefWorkloads = new List<(ChefActor Chef, int Workload)>();
        foreach (var chef in _chefs.Values)
        {
            var workload = await chef.GetWorkloadAsync();
            chefWorkloads.Add((chef, workload));
        }
        
        var leastBusyChef = chefWorkloads.OrderBy(x => x.Workload).First().Chef;

        await leastBusyChef.ProcessOrderAsync(order);
        Console.WriteLine($"   ✅ Assigned to {leastBusyChef.ActorId}");
        Console.WriteLine();
    }

    private static async Task CompleteOrder(string orderId)
    {
        // Find chef with this order
        foreach (var chef in _chefs.Values)
        {
            var activeOrders = await chef.GetActiveOrdersAsync();
            if (activeOrders.Contains(orderId))
            {
                await chef.CompleteOrderAsync(orderId);
                Console.WriteLine($"✅ Order {orderId} completed by {chef.ActorId}");
                return;
            }
        }

        Console.WriteLine($"❌ Order {orderId} not found in any chef's queue");
    }

    private static void ShowQueueStatus()
    {
        Console.WriteLine($"📊 Queue Status:");
        Console.WriteLine($"   Pending Orders: {_orderQueue.Count}");
        
        if (_orderQueue.Any())
        {
            Console.WriteLine($"   Next Orders:");
            foreach (var order in _orderQueue.Take(5))
            {
                Console.WriteLine($"      • {order.OrderId} - {order.PizzaType}");
            }
        }
        Console.WriteLine();
    }

    private static async Task ShowChefWorkload()
    {
        Console.WriteLine($"👨‍🍳 Chef Workload:");
        foreach (var chef in _chefs.Values)
        {
            var workload = await chef.GetWorkloadAsync();
            var activeOrders = await chef.GetActiveOrdersAsync();
            
            Console.WriteLine($"   {chef.ActorId}: {workload} active orders");
            if (activeOrders.Any())
            {
                foreach (var orderId in activeOrders)
                {
                    Console.WriteLine($"      • {orderId}");
                }
            }
        }
        Console.WriteLine();
    }

    private static async Task StreamConsumerLoop(CancellationToken cancellationToken)
    {
        // In a real implementation, this would:
        // 1. Subscribe to "kitchen/new-orders" stream via IQuarkStreamProvider
        // 2. Receive messages automatically via implicit subscriptions
        // 3. Process each message by assigning to available chef

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                // Simulated stream processing happens here
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
