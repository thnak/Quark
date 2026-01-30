# Backpressure & Flow Control in Quark Streaming

This document demonstrates the backpressure and flow control features implemented in Phase 8.5.

## Overview

Quark's streaming system provides adaptive backpressure to handle scenarios where consumers can't keep pace with producers. This prevents system overwhelm and provides intelligent flow control.

## Available Backpressure Modes

### 1. None (Default)
No backpressure - messages are delivered immediately as they're published. This is the default mode for backward compatibility.

```csharp
var provider = new QuarkStreamProvider();
var stream = provider.GetStream<string>("events", "key");
// No backpressure configuration needed - uses default mode
```

### 2. DropOldest
Drops oldest buffered messages when the buffer is full. Ensures new messages are always accepted.

```csharp
var provider = new QuarkStreamProvider();
provider.ConfigureBackpressure("events", new StreamBackpressureOptions
{
    Mode = BackpressureMode.DropOldest,
    BufferSize = 1000,
    EnableMetrics = true
});

var stream = provider.GetStream<string>("events", "sensor-1");
await stream.PublishAsync("new data"); // Always succeeds, may drop old data
```

**Use Case**: High-frequency sensor data where latest values are most important.

### 3. DropNewest
Drops newest messages when the buffer is full. Preserves older messages.

```csharp
provider.ConfigureBackpressure("orders", new StreamBackpressureOptions
{
    Mode = BackpressureMode.DropNewest,
    BufferSize = 500,
    EnableMetrics = true
});

var stream = provider.GetStream<Order>("orders", "customer-123");
bool accepted = await stream.PublishAsync(order);
if (!accepted)
{
    // Handle rejection - order wasn't added to buffer
    Console.WriteLine("Order rejected due to backpressure");
}
```

**Use Case**: Order processing where earlier orders have priority.

### 4. Block
Blocks publishers when the buffer is full. Provides guaranteed delivery by slowing down producers.

```csharp
provider.ConfigureBackpressure("transactions", new StreamBackpressureOptions
{
    Mode = BackpressureMode.Block,
    BufferSize = 100,
    EnableMetrics = true
});

var stream = provider.GetStream<Transaction>("transactions", "account-456");
await stream.PublishAsync(transaction); // May block if buffer is full
```

**Use Case**: Financial transactions where every message must be delivered.

### 5. Throttle
Rate-limits message publishing based on time windows.

```csharp
provider.ConfigureBackpressure("notifications", new StreamBackpressureOptions
{
    Mode = BackpressureMode.Throttle,
    MaxMessagesPerWindow = 100,
    ThrottleWindow = TimeSpan.FromSeconds(1),
    BufferSize = 500,
    EnableMetrics = true
});

var stream = provider.GetStream<Notification>("notifications", "user-789");
await stream.PublishAsync(notification); // Throttled to 100 messages/second
```

**Use Case**: Rate-limited APIs or notification systems.

## Monitoring Backpressure

All backpressure modes (except None with metrics disabled) provide real-time metrics:

```csharp
var stream = provider.GetStream<Event>("events", "key");

// Publish some messages
for (int i = 0; i < 1000; i++)
{
    await stream.PublishAsync(new Event { Id = i });
}

// Check metrics
var metrics = stream.BackpressureMetrics;
Console.WriteLine($"Messages Published: {metrics.MessagesPublished}");
Console.WriteLine($"Messages Dropped: {metrics.MessagesDropped}");
Console.WriteLine($"Throttle Events: {metrics.ThrottleEvents}");
Console.WriteLine($"Current Buffer Depth: {metrics.CurrentBufferDepth}");
Console.WriteLine($"Peak Buffer Depth: {metrics.PeakBufferDepth}");
Console.WriteLine($"Last Updated: {metrics.LastUpdated}");
```

## Configuration Options

```csharp
public sealed class StreamBackpressureOptions
{
    // Backpressure mode (None, DropOldest, DropNewest, Block, Throttle)
    public BackpressureMode Mode { get; set; } = BackpressureMode.None;
    
    // Maximum buffer size for pending messages
    public int BufferSize { get; set; } = 1000;
    
    // Throttle: Max messages per window
    public int MaxMessagesPerWindow { get; set; } = 100;
    
    // Throttle: Time window for rate limiting
    public TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromSeconds(1);
    
    // Enable metrics collection
    public bool EnableMetrics { get; set; } = true;
}
```

## Per-Namespace Configuration

Backpressure is configured per namespace, allowing different policies for different stream types:

```csharp
var provider = new QuarkStreamProvider();

// High-frequency sensor data - drop old values
provider.ConfigureBackpressure("sensors", new StreamBackpressureOptions
{
    Mode = BackpressureMode.DropOldest,
    BufferSize = 500
});

// Critical transactions - guarantee delivery
provider.ConfigureBackpressure("transactions", new StreamBackpressureOptions
{
    Mode = BackpressureMode.Block,
    BufferSize = 100
});

// Rate-limited notifications
provider.ConfigureBackpressure("notifications", new StreamBackpressureOptions
{
    Mode = BackpressureMode.Throttle,
    MaxMessagesPerWindow = 50,
    ThrottleWindow = TimeSpan.FromSeconds(1)
});

// Now use streams with their configured backpressure
var sensorStream = provider.GetStream<SensorData>("sensors", "temp-1");
var txStream = provider.GetStream<Transaction>("transactions", "acct-1");
var notifStream = provider.GetStream<Notification>("notifications", "user-1");
```

## Slow Consumer Example

```csharp
var provider = new QuarkStreamProvider();
provider.ConfigureBackpressure("events", new StreamBackpressureOptions
{
    Mode = BackpressureMode.DropOldest,
    BufferSize = 10,
    EnableMetrics = true
});

var stream = provider.GetStream<string>("events", "key");

// Subscribe with a slow consumer
await stream.SubscribeAsync(async msg =>
{
    // Simulate slow processing
    await Task.Delay(100);
    Console.WriteLine($"Processed: {msg}");
});

// Publish messages rapidly
for (int i = 0; i < 50; i++)
{
    await stream.PublishAsync($"Message {i}");
}

// Wait for processing
await Task.Delay(2000);

// Check how many were dropped
var metrics = stream.BackpressureMetrics!;
Console.WriteLine($"Published: {metrics.MessagesPublished}");
Console.WriteLine($"Dropped: {metrics.MessagesDropped}");
Console.WriteLine($"Delivered: {metrics.MessagesPublished - metrics.MessagesDropped}");
```

## AOT Compatibility

All backpressure implementations are fully Native AOT compatible:
- Uses `System.Threading.Channels` for buffering (AOT-safe)
- No runtime reflection
- Source-generated dispatchers for stream consumers
- Zero-allocation metrics tracking

## Performance Considerations

- **Buffer Size**: Larger buffers reduce drops but increase memory usage
- **Drop Strategies**: DropOldest and DropNewest have minimal overhead
- **Block Strategy**: May slow down publishers; use for critical data only
- **Throttle Strategy**: Adds timestamp tracking overhead; use for rate-limited scenarios
- **Metrics**: Minimal overhead; can be disabled if not needed

## Best Practices

1. **Choose the Right Mode**:
   - Use `DropOldest` for time-series data where latest values matter
   - Use `DropNewest` for queues where FIFO ordering is critical
   - Use `Block` for critical data that must not be lost
   - Use `Throttle` for rate-limited external APIs

2. **Size Buffers Appropriately**:
   - Match buffer size to expected burst patterns
   - Monitor peak buffer depth and adjust accordingly
   - Larger buffers = more memory, better burst handling

3. **Monitor Metrics**:
   - Track dropped messages in production
   - Alert on persistent high buffer depths
   - Use metrics to tune buffer sizes

4. **Test Under Load**:
   - Simulate slow consumers in tests
   - Verify backpressure behavior under stress
   - Validate metrics accuracy

## Integration with Actor Streaming

Backpressure works seamlessly with implicit actor subscriptions:

```csharp
[Actor(Name = "OrderProcessor")]
[QuarkStream("orders/new")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<Order>
{
    public OrderProcessorActor(string actorId) : base(actorId) { }

    public async Task OnStreamMessageAsync(
        Order message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        // Process order - backpressure is handled automatically
        await ProcessOrderAsync(message);
    }
}

// Configure backpressure for the stream namespace
provider.ConfigureBackpressure("orders", new StreamBackpressureOptions
{
    Mode = BackpressureMode.Block,
    BufferSize = 100
});
```

The backpressure system automatically manages message delivery to actor consumers, ensuring they're not overwhelmed even under high load.
