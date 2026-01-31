using Microsoft.Extensions.Logging;
using Quark.Sagas;

namespace Quark.Examples.Sagas;

/// <summary>
/// Example demonstrating a distributed order processing saga.
/// This shows how sagas orchestrate multi-step transactions with automatic compensation.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Saga Orchestration Example ===\n");

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<Program>();
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<OrderContext>(stateStore, loggerFactory.CreateLogger<SagaCoordinator<OrderContext>>());

        // Example 1: Successful order processing
        Console.WriteLine("Example 1: Successful Order Processing");
        Console.WriteLine("---------------------------------------");
        await RunSuccessfulOrder(coordinator, stateStore, logger);

        Console.WriteLine("\n\n");

        // Example 2: Order with payment failure (triggers compensation)
        Console.WriteLine("Example 2: Order with Payment Failure");
        Console.WriteLine("--------------------------------------");
        await RunFailedPayment(coordinator, stateStore, logger);

        Console.WriteLine("\n\n");

        // Example 3: Order with inventory failure (triggers compensation)
        Console.WriteLine("Example 3: Order with Inventory Failure");
        Console.WriteLine("----------------------------------------");
        await RunFailedInventory(coordinator, stateStore, logger);

        Console.WriteLine("\n\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task RunSuccessfulOrder(
        SagaCoordinator<OrderContext> coordinator,
        InMemorySagaStateStore stateStore,
        ILogger logger)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var saga = new OrderProcessingSaga(orderId, stateStore, logger);

        var context = new OrderContext
        {
            OrderId = orderId,
            CustomerId = "customer-123",
            Amount = 99.99m,
            Items = new List<string> { "Item-A", "Item-B" }
        };

        logger.LogInformation("Starting order {OrderId} for ${Amount}", orderId, context.Amount);

        var status = await coordinator.StartSagaAsync(saga, context);

        logger.LogInformation("Order {OrderId} completed with status: {Status}", orderId, status);
        logger.LogInformation("Final state: Payment={Payment}, Inventory={Inventory}, Shipment={Shipment}",
            context.PaymentCompleted, context.InventoryReserved, context.ShipmentScheduled);
    }

    private static async Task RunFailedPayment(
        SagaCoordinator<OrderContext> coordinator,
        InMemorySagaStateStore stateStore,
        ILogger logger)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var saga = new OrderProcessingSaga(orderId, stateStore, logger, failAtStep: "ProcessPayment");

        var context = new OrderContext
        {
            OrderId = orderId,
            CustomerId = "customer-456",
            Amount = 150.00m,
            Items = new List<string> { "Item-C" }
        };

        logger.LogInformation("Starting order {OrderId} for ${Amount} (payment will fail)", orderId, context.Amount);

        var status = await coordinator.StartSagaAsync(saga, context);

        logger.LogInformation("Order {OrderId} completed with status: {Status}", orderId, status);
        logger.LogInformation("All operations should be compensated");
    }

    private static async Task RunFailedInventory(
        SagaCoordinator<OrderContext> coordinator,
        InMemorySagaStateStore stateStore,
        ILogger logger)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var saga = new OrderProcessingSaga(orderId, stateStore, logger, failAtStep: "ReserveInventory");

        var context = new OrderContext
        {
            OrderId = orderId,
            CustomerId = "customer-789",
            Amount = 75.50m,
            Items = new List<string> { "Item-D", "Item-E" }
        };

        logger.LogInformation("Starting order {OrderId} for ${Amount} (inventory will fail)", orderId, context.Amount);

        var status = await coordinator.StartSagaAsync(saga, context);

        logger.LogInformation("Order {OrderId} completed with status: {Status}", orderId, status);
        logger.LogInformation("Payment should be refunded (compensated)");
        logger.LogInformation("Payment refunded: {Refunded}", !context.PaymentCompleted);
    }
}
