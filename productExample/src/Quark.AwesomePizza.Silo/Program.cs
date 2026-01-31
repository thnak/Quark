using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo;

/// <summary>
/// Awesome Pizza Silo - A Native AOT console application that hosts actor instances.
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
        Console.WriteLine("║       Awesome Pizza - Quark Silo Host                   ║");
        Console.WriteLine("║       High-Performance Native AOT Actor System           ║");
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

        Console.WriteLine("✅ Silo is ready to host actors");
        Console.WriteLine("📋 Actor types registered: Order, Driver, Chef, Kitchen, Inventory, Restaurant");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  create-order <orderId> <customerId> <restaurantId> - Create new order");
        Console.WriteLine("  create-driver <driverId> <name>                    - Initialize driver");
        Console.WriteLine("  create-chef <chefId> <name>                        - Initialize chef");
        Console.WriteLine("  status <orderId> <newStatus>                       - Update order status");
        Console.WriteLine("  list                                               - List active actors");
        Console.WriteLine("  exit                                               - Shutdown silo");
        Console.WriteLine();

        // Command loop
        await CommandLoop();

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
                    case "create-order" when parts.Length >= 4:
                        await CreateOrderAsync(parts[1], parts[2], parts[3]);
                        break;

                    case "create-driver" when parts.Length >= 3:
                        await CreateDriverAsync(parts[1], string.Join(" ", parts.Skip(2)));
                        break;

                    case "create-chef" when parts.Length >= 3:
                        await CreateChefAsync(parts[1], string.Join(" ", parts.Skip(2)));
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

    private static async Task CreateOrderAsync(string orderId, string customerId, string restaurantId)
    {
        if (_factory == null)
            throw new InvalidOperationException("Factory not initialized");

        var actor = _factory.CreateActor<OrderActor>(orderId);
        
        if (!ActiveActors.ContainsKey(orderId))
        {
            await actor.OnActivateAsync();
            ActiveActors[orderId] = actor;
        }

        var request = new CreateOrderRequest(
            CustomerId: customerId,
            RestaurantId: restaurantId,
            Items: new List<PizzaItem>
            {
                new("Margherita", "Medium", new List<string> { "cheese", "tomato" }, 1, 12.99m)
            },
            DeliveryAddress: new GpsLocation(40.7128, -74.0060, DateTime.UtcNow));

        var response = await actor.CreateOrderAsync(request);

        Console.WriteLine($"✅ Order created: {orderId}");
        Console.WriteLine($"   Customer: {customerId}");
        Console.WriteLine($"   Restaurant: {restaurantId}");
        Console.WriteLine($"   Status: {response.State.Status}");
        Console.WriteLine($"   Total: ${response.State.TotalAmount:F2}");
        Console.WriteLine($"   ETA: {response.EstimatedDeliveryTime:HH:mm:ss}");
    }

    private static async Task CreateDriverAsync(string driverId, string name)
    {
        if (_factory == null)
            throw new InvalidOperationException("Factory not initialized");

        var actor = _factory.CreateActor<DriverActor>(driverId);
        
        if (!ActiveActors.ContainsKey(driverId))
        {
            await actor.OnActivateAsync();
            ActiveActors[driverId] = actor;
        }

        var driver = await actor.InitializeAsync(name);

        Console.WriteLine($"✅ Driver created: {driverId}");
        Console.WriteLine($"   Name: {driver.Name}");
        Console.WriteLine($"   Status: {driver.Status}");
    }

    private static async Task CreateChefAsync(string chefId, string name)
    {
        if (_factory == null)
            throw new InvalidOperationException("Factory not initialized");

        var actor = _factory.CreateActor<ChefActor>(chefId);
        
        if (!ActiveActors.ContainsKey(chefId))
        {
            await actor.OnActivateAsync();
            ActiveActors[chefId] = actor;
        }

        var chef = await actor.InitializeAsync(name);

        Console.WriteLine($"✅ Chef created: {chefId}");
        Console.WriteLine($"   Name: {chef.Name}");
        Console.WriteLine($"   Status: {chef.Status}");
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

        var order = await actor.UpdateStatusAsync(new UpdateStatusRequest(newStatus));
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
