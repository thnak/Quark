using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

namespace Quark.Demo.PizzaDash.Silo;

/// <summary>
/// Kitchen Silo - A Native AOT console application that hosts actor instances.
/// This represents a single node in the distributed cluster.
/// </summary>
internal abstract class Program
{
    private static IActorFactory? _factory;
    private static readonly Dictionary<string, IActor> ActiveActors = new();
    private static readonly CancellationTokenSource Cts = new();

    private static async Task Main()
    {
        var siloId = Environment.GetEnvironmentVariable("SILO_ID") ?? $"silo-{Guid.NewGuid():N}";
        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
        var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";

        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Quark Pizza Dash - Kitchen Silo                   ║");
        Console.WriteLine("║       High-Performance Native AOT Actor Host             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"🏭 Silo ID: {siloId}");
        Console.WriteLine($"🔌 Redis:   {redisHost}:{redisPort}");
        Console.WriteLine($"⚡ Native AOT: Enabled");
        Console.WriteLine($"🚀 Started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        // Initialize actor factory
        _factory = new ActorFactory();

        // Register shutdown handler
        Console.CancelKeyPress += async (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("🛑 Shutting down silo...");
            await ShutdownAsync();
        };

        // Start the reminder checker (simulates persistent reminders)
        var reminderTask = Task.Run(() => ReminderCheckerLoop(Cts.Token));

        Console.WriteLine("✅ Silo is ready to host actors");
        Console.WriteLine("📋 Waiting for actor placement requests...");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  create <orderId> <customerId> <pizzaType> - Create new order");
        Console.WriteLine("  status <orderId> <newStatus>                - Update order status");
        Console.WriteLine("  list                                        - List active actors");
        Console.WriteLine("  exit                                        - Shutdown silo");
        Console.WriteLine();

        // Command loop
        await CommandLoop();

        // Wait for reminder task to complete
        await reminderTask;

        Console.WriteLine("👋 Silo shutdown complete");
    }

    private static async Task CommandLoop()
    {
        while (!Cts.Token.IsCancellationRequested)
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
                    case "create" when parts.Length >= 4:
                        await CreateOrderAsync(parts[1], parts[2], string.Join(" ", parts.Skip(3)));
                        break;

                    case "status" when parts.Length >= 3:
                        await UpdateStatusAsync(parts[1], parts[2]);
                        break;

                    case "list":
                        ListActors();
                        break;

                    case "exit":
                    case "quit":
                        await Cts.CancelAsync();
                        break;

                    default:
                        Console.WriteLine("❌ Unknown command or invalid arguments");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }
    }

    private static async Task CreateOrderAsync(string orderId, string customerId, string pizzaType)
    {
        if (_factory == null)
            throw new InvalidOperationException("Factory not initialized");

        // Create or get the order actor
        var actor = _factory.CreateActor<OrderActor>(orderId);
        
        if (!ActiveActors.ContainsKey(orderId))
        {
            await actor.OnActivateAsync();
            ActiveActors[orderId] = actor;
        }

        // Create the order
        var order = await actor.CreateOrderAsync(customerId, pizzaType);

        Console.WriteLine($"✅ Order created: {orderId}");
        Console.WriteLine($"   Customer: {customerId}");
        Console.WriteLine($"   Pizza: {pizzaType}");
        Console.WriteLine($"   Status: {order.Status}");
        Console.WriteLine($"   Time: {order.OrderTime:HH:mm:ss}");
    }

    private static async Task UpdateStatusAsync(string orderId, string statusString)
    {
        if (!ActiveActors.TryGetValue(orderId, out var actorBase))
        {
            Console.WriteLine($"❌ Order {orderId} not found on this silo");
            return;
        }

        if (actorBase is not OrderActor actor)
        {
            Console.WriteLine($"❌ Actor {orderId} is not an OrderActor");
            return;
        }

        if (!Enum.TryParse<OrderStatus>(statusString, true, out var newStatus))
        {
            Console.WriteLine($"❌ Invalid status: {statusString}");
            Console.WriteLine($"   Valid: {string.Join(", ", Enum.GetNames<OrderStatus>())}");
            return;
        }

        var order = await actor.UpdateStatusAsync(newStatus);
        Console.WriteLine($"✅ Order {orderId} updated to: {order.Status}");
        Console.WriteLine($"   Updated at: {order.LastUpdated:HH:mm:ss}");
    }

    private static void ListActors()
    {
        Console.WriteLine($"📋 Active actors on this silo: {ActiveActors.Count}");
        foreach (var kvp in ActiveActors)
        {
            Console.WriteLine($"   • {kvp.Value.GetType().Name}: {kvp.Key}");
        }
    }

    private static async Task ReminderCheckerLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

                // Check all active orders for late delivery
                foreach (var kvp in ActiveActors.ToList())
                {
                    if (kvp.Value is OrderActor orderActor)
                    {
                        var isLate = await orderActor.IsOrderLateAsync();
                        if (isLate)
                        {
                            Console.WriteLine($"⚠️  ALERT: Order {kvp.Key} has been in oven for >15 minutes!");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Reminder checker error: {ex.Message}");
            }
        }
    }

    private static async Task ShutdownAsync()
    {
        await Cts.CancelAsync();

        // Deactivate all actors
        foreach (var kvp in ActiveActors.ToList())
        {
            try
            {
                await kvp.Value.OnDeactivateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error deactivating {kvp.Key}: {ex.Message}");
            }
        }

        ActiveActors.Clear();
    }
}
