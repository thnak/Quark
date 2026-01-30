using Quark.Abstractions.Streaming;
using Quark.Core.Streaming;

namespace Quark.Examples.Backpressure;

/// <summary>
/// Example demonstrating backpressure and flow control in Quark streaming.
/// Shows different backpressure strategies for handling slow consumers.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Backpressure & Flow Control Demo ===\n");

        await DemoNoBackpressure();
        await DemoDropOldest();
        await DemoDropNewest();
        await DemoBlock();
        await DemoThrottle();

        Console.WriteLine("\n=== Demo Complete ===");
    }

    private static async Task DemoNoBackpressure()
    {
        Console.WriteLine("1. No Backpressure (Default)");
        Console.WriteLine("   Messages delivered immediately, no flow control");
        Console.WriteLine();

        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("events", "demo");

        var received = 0;
        await stream.SubscribeAsync(async msg =>
        {
            received++;
            await Task.CompletedTask;
        });

        // Publish rapidly
        for (int i = 0; i < 10; i++)
        {
            await stream.PublishAsync($"Message {i}");
        }

        await Task.Delay(100);
        Console.WriteLine($"   Published: 10, Received: {received}");
        Console.WriteLine();
    }

    private static async Task DemoDropOldest()
    {
        Console.WriteLine("2. DropOldest Strategy");
        Console.WriteLine("   Drops oldest messages when buffer is full (keeps newest)");
        Console.WriteLine();

        var provider = new QuarkStreamProvider();
        provider.ConfigureBackpressure("sensors", new StreamBackpressureOptions
        {
            Mode = BackpressureMode.DropOldest,
            BufferSize = 5,
            EnableMetrics = true
        });

        var stream = provider.GetStream<string>("sensors", "temp-1");

        var received = new List<string>();
        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(100); // Slow consumer
            lock (received) { received.Add(msg); }
        });

        // Publish more than buffer size
        for (int i = 0; i < 15; i++)
        {
            await stream.PublishAsync($"Temp-{i}°C");
        }

        await Task.Delay(2000);

        var metrics = stream.BackpressureMetrics!;
        Console.WriteLine($"   Published: {metrics.MessagesPublished}");
        Console.WriteLine($"   Dropped: {metrics.MessagesDropped}");
        Console.WriteLine($"   Received: {received.Count}");
        Console.WriteLine($"   Last message: {received.LastOrDefault()}");
        Console.WriteLine($"   (Note: Keeps newest data, drops oldest)");
        Console.WriteLine();
    }

    private static async Task DemoDropNewest()
    {
        Console.WriteLine("3. DropNewest Strategy");
        Console.WriteLine("   Drops newest messages when buffer is full (keeps oldest)");
        Console.WriteLine();

        var provider = new QuarkStreamProvider();
        provider.ConfigureBackpressure("orders", new StreamBackpressureOptions
        {
            Mode = BackpressureMode.DropNewest,
            BufferSize = 5,
            EnableMetrics = true
        });

        var stream = provider.GetStream<string>("orders", "customer-1");

        var received = new List<string>();
        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(100); // Slow consumer
            lock (received) { received.Add(msg); }
        });

        // Publish more than buffer size
        for (int i = 0; i < 15; i++)
        {
            await stream.PublishAsync($"Order-{i}");
        }

        await Task.Delay(2000);

        var metrics = stream.BackpressureMetrics!;
        Console.WriteLine($"   Published: {metrics.MessagesPublished - metrics.MessagesDropped}");
        Console.WriteLine($"   Dropped: {metrics.MessagesDropped}");
        Console.WriteLine($"   Received: {received.Count}");
        Console.WriteLine($"   First message: {received.FirstOrDefault()}");
        Console.WriteLine($"   (Note: Preserves oldest data, drops newest)");
        Console.WriteLine();
    }

    private static async Task DemoBlock()
    {
        Console.WriteLine("4. Block Strategy");
        Console.WriteLine("   Blocks publishers when buffer is full (guaranteed delivery)");
        Console.WriteLine();

        var provider = new QuarkStreamProvider();
        provider.ConfigureBackpressure("transactions", new StreamBackpressureOptions
        {
            Mode = BackpressureMode.Block,
            BufferSize = 3,
            EnableMetrics = true
        });

        var stream = provider.GetStream<string>("transactions", "account-1");

        var received = 0;
        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(100); // Slow consumer
            Interlocked.Increment(ref received);
        });

        var startTime = DateTime.UtcNow;
        
        // Publish - will block when buffer is full
        for (int i = 0; i < 10; i++)
        {
            await stream.PublishAsync($"TX-{i}");
        }

        var elapsed = DateTime.UtcNow - startTime;

        await Task.Delay(1500);

        var metrics = stream.BackpressureMetrics!;
        Console.WriteLine($"   Published: {metrics.MessagesPublished}");
        Console.WriteLine($"   Dropped: {metrics.MessagesDropped}");
        Console.WriteLine($"   Received: {received}");
        Console.WriteLine($"   Publish time: {elapsed.TotalMilliseconds:F0}ms");
        Console.WriteLine($"   (Note: All messages delivered, publishers blocked)");
        Console.WriteLine();
    }

    private static async Task DemoThrottle()
    {
        Console.WriteLine("5. Throttle Strategy");
        Console.WriteLine("   Rate-limits message publishing");
        Console.WriteLine();

        var provider = new QuarkStreamProvider();
        provider.ConfigureBackpressure("notifications", new StreamBackpressureOptions
        {
            Mode = BackpressureMode.Throttle,
            MaxMessagesPerWindow = 5,
            ThrottleWindow = TimeSpan.FromSeconds(1),
            BufferSize = 50,
            EnableMetrics = true
        });

        var stream = provider.GetStream<string>("notifications", "user-1");

        var received = 0;
        await stream.SubscribeAsync(async msg =>
        {
            Interlocked.Increment(ref received);
            await Task.CompletedTask;
        });

        var startTime = DateTime.UtcNow;

        // Publish rapidly - will be throttled
        for (int i = 0; i < 12; i++)
        {
            await stream.PublishAsync($"Notification-{i}");
        }

        var elapsed = DateTime.UtcNow - startTime;

        await Task.Delay(1500);

        var metrics = stream.BackpressureMetrics!;
        Console.WriteLine($"   Published: {metrics.MessagesPublished}");
        Console.WriteLine($"   Throttle events: {metrics.ThrottleEvents}");
        Console.WriteLine($"   Received: {received}");
        Console.WriteLine($"   Publish time: {elapsed.TotalMilliseconds:F0}ms");
        Console.WriteLine($"   (Note: Limited to 5 messages/second)");
        Console.WriteLine();
    }
}
