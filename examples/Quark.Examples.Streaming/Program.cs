using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;
using Quark.Core.Streaming;

namespace Quark.Examples.Streaming;

/// <summary>
/// Example demonstrating Quark Streams - both implicit and explicit pub/sub patterns.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Quark Phase 5: Reactive Streaming Example          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Initialize the actor factory and stream provider
        var actorFactory = new ActorFactory();
        var streamProvider = new QuarkStreamProvider(actorFactory);
        
        // Set up the global stream registry (for source generator integration)
        StreamRegistry.SetBroker(streamProvider.Broker);

        // Register implicit subscriptions manually
        // (In a real app, this would be done by the source generator)
        streamProvider.Broker.RegisterImplicitSubscription(
            "orders/processed", 
            typeof(OrderProcessorActor), 
            typeof(OrderMessage));

        streamProvider.Broker.RegisterImplicitSubscription(
            "notifications/user", 
            typeof(NotificationActor), 
            typeof(string));

        Console.WriteLine("═══ Example 1: Implicit Subscriptions ═══");
        Console.WriteLine("Actors are automatically activated when messages arrive on their subscribed streams.\n");
        
        await DemoImplicitSubscriptions(streamProvider);

        Console.WriteLine("\n═══ Example 2: Explicit Pub/Sub ═══");
        Console.WriteLine("Dynamic subscriptions that can be created and destroyed at runtime.\n");
        
        await DemoExplicitPubSub(streamProvider);

        Console.WriteLine("\n═══ Example 3: Multiple Subscribers ═══");
        Console.WriteLine("Multiple actors/handlers can subscribe to the same stream.\n");
        
        await DemoMultipleSubscribers(streamProvider);

        Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  All Examples Complete!                    ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    }

    static async Task DemoImplicitSubscriptions(QuarkStreamProvider provider)
    {
        Console.WriteLine("Publishing order message to 'orders/processed' stream...");
        
        var orderStream = provider.GetStream<OrderMessage>("orders/processed", "order-123");
        var order = new OrderMessage
        {
            OrderId = "order-123",
            CustomerName = "John Doe",
            TotalAmount = 99.99m
        };

        await orderStream.PublishAsync(order);
        await Task.Delay(100); // Give time for async processing

        Console.WriteLine("✓ Order processed by OrderProcessorActor (implicitly activated)");
        Console.WriteLine();

        Console.WriteLine("Publishing notification to 'notifications/user' stream...");
        var notificationStream = provider.GetStream<string>("notifications/user", "user-456");
        await notificationStream.PublishAsync("Your order has been shipped!");
        await Task.Delay(100);

        Console.WriteLine("✓ Notification sent to NotificationActor (implicitly activated)");
    }

    static async Task DemoExplicitPubSub(QuarkStreamProvider provider)
    {
        var receivedMessages = new List<string>();
        
        Console.WriteLine("Creating explicit subscription to 'events/system' stream...");
        var eventStream = provider.GetStream<string>("events/system", "server-1");
        
        var subscription = await eventStream.SubscribeAsync(async message =>
        {
            receivedMessages.Add(message);
            Console.WriteLine($"  → Received: {message}");
            await Task.CompletedTask;
        });

        Console.WriteLine("Publishing events...");
        await eventStream.PublishAsync("Server started");
        await eventStream.PublishAsync("Database connected");
        await eventStream.PublishAsync("Ready to serve requests");

        await Task.Delay(100);
        Console.WriteLine($"✓ Received {receivedMessages.Count} messages through explicit subscription");

        Console.WriteLine("\nUnsubscribing...");
        await subscription.UnsubscribeAsync();
        
        await eventStream.PublishAsync("This won't be received");
        await Task.Delay(100);
        
        Console.WriteLine($"✓ Still have {receivedMessages.Count} messages (unsubscribed successfully)");
    }

    static async Task DemoMultipleSubscribers(QuarkStreamProvider provider)
    {
        var subscriber1Messages = new List<string>();
        var subscriber2Messages = new List<string>();

        Console.WriteLine("Creating two subscribers to 'chat/lobby' stream...");
        var chatStream = provider.GetStream<string>("chat/lobby", "lobby-1");

        var sub1 = await chatStream.SubscribeAsync(async msg =>
        {
            subscriber1Messages.Add(msg);
            Console.WriteLine($"  [Subscriber 1] {msg}");
            await Task.CompletedTask;
        });

        var sub2 = await chatStream.SubscribeAsync(async msg =>
        {
            subscriber2Messages.Add(msg);
            Console.WriteLine($"  [Subscriber 2] {msg}");
            await Task.CompletedTask;
        });

        Console.WriteLine("\nPublishing chat messages...");
        await chatStream.PublishAsync("User Alice joined");
        await chatStream.PublishAsync("User Bob joined");
        await chatStream.PublishAsync("Alice: Hello everyone!");

        await Task.Delay(100);
        Console.WriteLine($"✓ Subscriber 1 received {subscriber1Messages.Count} messages");
        Console.WriteLine($"✓ Subscriber 2 received {subscriber2Messages.Count} messages");

        // Clean up
        await sub1.UnsubscribeAsync();
        await sub2.UnsubscribeAsync();
    }
}

// ═══ Message Types ═══

public class OrderMessage
{
    public string OrderId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
}

// ═══ Actor Implementations ═══

/// <summary>
/// Actor that automatically processes orders from the stream.
/// Uses implicit subscription via [QuarkStream] attribute.
/// </summary>
[Actor(Name = "OrderProcessor")]
[QuarkStream("orders/processed")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<OrderMessage>
{
    public OrderProcessorActor(string actorId) : base(actorId)
    {
    }

    public async Task OnStreamMessageAsync(
        OrderMessage message, 
        StreamId streamId, 
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  [OrderProcessor-{ActorId}] Processing order {message.OrderId}");
        Console.WriteLine($"    Customer: {message.CustomerName}");
        Console.WriteLine($"    Amount: ${message.TotalAmount}");
        
        // Simulate some processing
        await Task.Delay(50, cancellationToken);
    }
}

/// <summary>
/// Actor that sends notifications to users.
/// Uses implicit subscription via [QuarkStream] attribute.
/// </summary>
[Actor(Name = "Notification")]
[QuarkStream("notifications/user")]
public class NotificationActor : ActorBase, IStreamConsumer<string>
{
    public NotificationActor(string actorId) : base(actorId)
    {
    }

    public async Task OnStreamMessageAsync(
        string message, 
        StreamId streamId, 
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  [Notification-{ActorId}] Sending notification:");
        Console.WriteLine($"    Message: {message}");
        
        await Task.Delay(50, cancellationToken);
    }
}
