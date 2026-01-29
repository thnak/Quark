# Streaming in Quark

Quark Streams provide a reactive, decoupled messaging pattern where actors can publish and subscribe to data streams without direct knowledge of each other.

## Core Concepts

### What is Streaming?

Streaming in Quark enables:
- **Decoupled Communication**: Producers and consumers don't need references to each other
- **One-to-Many**: Multiple subscribers can receive the same message
- **Reactive Processing**: Actors activate automatically when messages arrive
- **Type-Safe**: Full generic type support with AOT compatibility

### Stream vs Direct Calls

**Direct Actor Calls:**
```csharp
var orderActor = factory.CreateActor<OrderActor>("order-123");
await orderActor.ProcessOrderAsync(order); // Tight coupling
```

**Streams:**
```csharp
await stream.PublishAsync(order); // Loose coupling - any subscriber gets it
```

## Quick Start

### Implicit Subscriptions (Auto-Activation)

The simplest way to use streams is with the `[QuarkStream]` attribute:

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Core.Streaming;

// Define an actor that subscribes to a stream
[Actor(Name = "OrderProcessor")]
[QuarkStream("orders/new")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<Order>
{
    public OrderProcessorActor(string actorId) : base(actorId) { }

    // This method is called when a message arrives
    public async Task OnStreamMessageAsync(
        Order message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processing order {message.OrderId}");
        await Task.CompletedTask;
    }
}

// Publish to the stream
var streamProvider = new QuarkStreamProvider(actorFactory);
var stream = streamProvider.GetStream<Order>("orders/new", "key-1");
await stream.PublishAsync(new Order { OrderId = "123", Total = 99.99m });
```

When a message is published:
1. The source generator maps the namespace to the actor type
2. The `StreamBroker` activates the actor using the stream key as the actor ID
3. The actor's `OnStreamMessageAsync` is called with the message

## Stream Anatomy

### StreamId

A `StreamId` uniquely identifies a stream:

```csharp
public record StreamId(string Namespace, string Key);

// Examples
var orderStream = new StreamId("orders/new", "2024-01-29");
var userStream = new StreamId("users/activity", "user:123");
var deviceStream = new StreamId("iot/sensors", "device:42");
```

- **Namespace**: Logical grouping (e.g., "orders/new", "chat/messages")
- **Key**: Specific stream instance (e.g., date, user ID, device ID)

### IStreamHandle<T>

A handle for publishing and subscribing to a specific stream:

```csharp
// Get a stream handle
IStreamHandle<string> stream = streamProvider.GetStream<string>("chat/lobby", "lobby-1");

// Publish messages
await stream.PublishAsync("Hello, world!");

// Subscribe
var subscription = await stream.SubscribeAsync(async message =>
{
    Console.WriteLine($"Received: {message}");
});

// Unsubscribe
await subscription.UnsubscribeAsync();
```

### IQuarkStreamProvider

The service for accessing streams:

```csharp
public interface IQuarkStreamProvider
{
    IStreamHandle<T> GetStream<T>(string namespace, string key);
}
```

## Implicit Subscriptions

### How It Works

1. **Mark actors** with `[QuarkStream]`:
```csharp
[QuarkStream("namespace")]
public class MyActor : ActorBase, IStreamConsumer<TMessage>
```

2. **Source generator** creates mappings at compile-time

3. **StreamBroker** routes messages to actors

### Example: Order Processing Pipeline

```csharp
// Order created → order/created
[Actor]
[QuarkStream("orders/created")]
public class OrderValidatorActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(
        Order message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Validating order {message.OrderId}");

        // Validate and publish to next stage
        if (ValidateOrder(message))
        {
            var streamProvider = new QuarkStreamProvider(_actorFactory!);
            var nextStream = streamProvider.GetStream<Order>("orders/validated", message.OrderId);
            await nextStream.PublishAsync(message);
        }
    }
}

// Order validated → orders/validated
[Actor]
[QuarkStream("orders/validated")]
public class PaymentProcessorActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(
        Order message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processing payment for {message.OrderId}");

        // Process payment and publish to next stage
        if (await ProcessPaymentAsync(message))
        {
            var streamProvider = new QuarkStreamProvider(_actorFactory!);
            var nextStream = streamProvider.GetStream<Order>("orders/paid", message.OrderId);
            await nextStream.PublishAsync(message);
        }
    }
}

// Order paid → orders/paid
[Actor]
[QuarkStream("orders/paid")]
public class FulfillmentActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(
        Order message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Fulfilling order {message.OrderId}");
        await ShipOrderAsync(message);
    }
}

// Start the pipeline
var streamProvider = new QuarkStreamProvider(actorFactory);
var stream = streamProvider.GetStream<Order>("orders/created", "order-123");
await stream.PublishAsync(new Order { OrderId = "order-123", Total = 99.99m });
```

### Multiple Consumers

Multiple actor types can subscribe to the same namespace:

```csharp
// Both receive messages on "orders/new"
[Actor]
[QuarkStream("orders/new")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(Order message, StreamId streamId, CancellationToken ct)
    {
        // Process the order
    }
}

[Actor]
[QuarkStream("orders/new")]
public class OrderAnalyticsActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(Order message, StreamId streamId, CancellationToken ct)
    {
        // Track analytics
    }
}

// Both actors receive the message
await stream.PublishAsync(order);
```

## Explicit Pub/Sub

For dynamic subscriptions that change at runtime:

### Subscribe Dynamically

```csharp
var streamProvider = new QuarkStreamProvider(actorFactory);

// Get a stream
var stream = streamProvider.GetStream<string>("notifications", "user:123");

// Subscribe with a callback
var subscription = await stream.SubscribeAsync(async message =>
{
    Console.WriteLine($"Notification: {message}");
    await ProcessNotificationAsync(message);
});

// Publish messages
await stream.PublishAsync("Your order has shipped");
await stream.PublishAsync("Payment received");

// Unsubscribe when done
await subscription.UnsubscribeAsync();
```

### Multiple Subscriptions

```csharp
var stream = streamProvider.GetStream<ChatMessage>("chat/room1", "messages");

// Subscriber 1: Logger
var logSub = await stream.SubscribeAsync(async msg =>
{
    await _logger.LogAsync($"[{msg.User}] {msg.Text}");
});

// Subscriber 2: Profanity filter
var filterSub = await stream.SubscribeAsync(async msg =>
{
    if (ContainsProfanity(msg.Text))
    {
        await _moderator.FlagMessageAsync(msg);
    }
});

// Subscriber 3: Analytics
var analyticsSub = await stream.SubscribeAsync(async msg =>
{
    await _analytics.TrackMessageAsync(msg);
});

// All three receive every message
await stream.PublishAsync(new ChatMessage { User = "Alice", Text = "Hello!" });

// Clean up
await logSub.UnsubscribeAsync();
await filterSub.UnsubscribeAsync();
await analyticsSub.UnsubscribeAsync();
```

## Use Cases

### Event Broadcasting

```csharp
// Server events
[Actor]
[QuarkStream("server/events")]
public class EventLoggerActor : ActorBase, IStreamConsumer<ServerEvent>
{
    public async Task OnStreamMessageAsync(ServerEvent message, StreamId streamId, CancellationToken ct)
    {
        await LogEventAsync(message);
    }
}

// Publish events from anywhere
var eventStream = streamProvider.GetStream<ServerEvent>("server/events", "server-1");
await eventStream.PublishAsync(new ServerEvent { Type = "Started", Timestamp = DateTime.UtcNow });
await eventStream.PublishAsync(new ServerEvent { Type = "ConnectionOpened", Timestamp = DateTime.UtcNow });
```

### IoT Data Processing

```csharp
// Temperature sensor data
[Actor]
[QuarkStream("iot/temperature")]
public class TemperatureMonitorActor : ActorBase, IStreamConsumer<SensorReading>
{
    public async Task OnStreamMessageAsync(SensorReading message, StreamId streamId, CancellationToken ct)
    {
        if (message.Value > 100)
        {
            await SendAlertAsync($"High temperature: {message.Value}°C");
        }
    }
}

// Device publishes readings
var sensorStream = streamProvider.GetStream<SensorReading>("iot/temperature", "sensor-42");
await sensorStream.PublishAsync(new SensorReading { Value = 75.5, Timestamp = DateTime.UtcNow });
```

### Real-time Notifications

```csharp
// User notifications
[Actor]
[QuarkStream("notifications/user")]
public class NotificationActor : ActorBase, IStreamConsumer<Notification>
{
    public async Task OnStreamMessageAsync(Notification message, StreamId streamId, CancellationToken ct)
    {
        // Actor ID is the user ID from stream key
        await SendToUserAsync(ActorId, message);
    }
}

// Send notification to specific user
var userStream = streamProvider.GetStream<Notification>("notifications/user", "user:123");
await userStream.PublishAsync(new Notification { Text = "New message received" });
```

### Fan-out Pattern

```csharp
// One message → many processors
[Actor]
[QuarkStream("tasks/work")]
public class WorkerActor : ActorBase, IStreamConsumer<WorkItem>
{
    public async Task OnStreamMessageAsync(WorkItem message, StreamId streamId, CancellationToken ct)
    {
        // Each worker processes independently
        await ProcessWorkItemAsync(message);
    }
}

// Distribute work to workers
for (int i = 1; i <= 10; i++)
{
    var workerStream = streamProvider.GetStream<WorkItem>("tasks/work", $"worker-{i}");
    await workerStream.PublishAsync(new WorkItem { Id = i });
}
```

## Advanced Patterns

### Stream Aggregation

Combine multiple streams:

```csharp
[Actor]
public class AggregatorActor : ActorBase
{
    private readonly List<IStreamSubscription> _subscriptions = new();

    public async Task SubscribeToAllSourcesAsync()
    {
        var streamProvider = new QuarkStreamProvider(_actorFactory!);

        // Subscribe to multiple source streams
        var sources = new[] { "sensors/temp", "sensors/humidity", "sensors/pressure" };

        foreach (var source in sources)
        {
            var stream = streamProvider.GetStream<SensorReading>(source, ActorId);
            var sub = await stream.SubscribeAsync(async reading =>
            {
                await ProcessReadingAsync(reading);
            });
            _subscriptions.Add(sub);
        }
    }

    private async Task ProcessReadingAsync(SensorReading reading)
    {
        // Aggregate readings from all sources
        await Task.CompletedTask;
    }
}
```

### Stream Transformation

Transform and republish:

```csharp
[Actor]
[QuarkStream("raw/events")]
public class TransformerActor : ActorBase, IStreamConsumer<RawEvent>
{
    public async Task OnStreamMessageAsync(RawEvent message, StreamId streamId, CancellationToken ct)
    {
        // Transform the event
        var processed = TransformEvent(message);

        // Publish to processed stream
        var streamProvider = new QuarkStreamProvider(_actorFactory!);
        var outputStream = streamProvider.GetStream<ProcessedEvent>("processed/events", streamId.Key);
        await outputStream.PublishAsync(processed);
    }
}
```

### Stream Filtering

Filter and forward:

```csharp
[Actor]
[QuarkStream("events/all")]
public class FilterActor : ActorBase, IStreamConsumer<Event>
{
    public async Task OnStreamMessageAsync(Event message, StreamId streamId, CancellationToken ct)
    {
        if (ShouldProcess(message))
        {
            var streamProvider = new QuarkStreamProvider(_actorFactory!);
            var outputStream = streamProvider.GetStream<Event>("events/filtered", message.Id);
            await outputStream.PublishAsync(message);
        }
    }

    private bool ShouldProcess(Event evt) => evt.Priority == Priority.High;
}
```

## Source Generation

The `StreamSourceGenerator` creates stream mappings at compile-time.

### What Gets Generated

For this actor:

```csharp
[QuarkStream("orders/new")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<Order>
{
    // Implementation
}
```

The generator creates:

```csharp
// Generated code (simplified)
public static class StreamRegistrationModule
{
    [ModuleInitializer]
    public static void Initialize()
    {
        StreamRegistry.RegisterImplicitSubscription(
            "orders/new",
            typeof(OrderProcessorActor),
            typeof(Order));
    }
}
```

### No Reflection

All stream routing is generated at compile-time:
- ✅ Native AOT compatible
- ✅ Type-safe message routing
- ✅ Zero runtime discovery overhead
- ✅ Compile-time validation

## Performance

### Throughput

- **Local delivery**: ~500K-1M messages/second
- **Multiple subscribers**: Linear scaling per subscriber
- **Serialization**: AOT-optimized JSON (zero reflection)

### Memory

- **Stream handle**: ~500 bytes overhead
- **Subscription**: ~200 bytes per subscription
- **Message queue**: Bounded (default 1000 messages)

### Latency

- **In-process**: <1ms end-to-end
- **With activation**: +2-5ms (actor creation)
- **Serialization**: ~10-50µs per message (type dependent)

## Best Practices

### 1. Use Meaningful Namespaces

```csharp
// ✅ Good: Clear, hierarchical
"orders/created"
"orders/validated"
"orders/shipped"
"users/activity"
"iot/sensors/temperature"

// ❌ Avoid: Vague, flat
"stream1"
"events"
"data"
```

### 2. Key Streams Appropriately

```csharp
// ✅ Good: Entity-specific keys
streamProvider.GetStream<Order>("orders/new", orderId);
streamProvider.GetStream<User>("users/activity", userId);

// ❌ Avoid: Shared keys (hot partition)
streamProvider.GetStream<Order>("orders/new", "shared");
```

### 3. Keep Messages Small

```csharp
// ✅ Good: Minimal data
public record OrderCreated(string OrderId, decimal Total);

// ❌ Avoid: Large embedded data
public record OrderCreated(string OrderId, byte[] FullOrderData); // Too big
```

### 4. Handle Errors Gracefully

```csharp
public async Task OnStreamMessageAsync(Order message, StreamId streamId, CancellationToken ct)
{
    try
    {
        await ProcessOrderAsync(message);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process order {OrderId}", message.OrderId);
        // Don't let errors crash the actor
    }
}
```

### 5. Unsubscribe When Done

```csharp
public class MyActor : ActorBase
{
    private IStreamSubscription? _subscription;

    public async Task SubscribeAsync()
    {
        var stream = _streamProvider.GetStream<Event>("events", ActorId);
        _subscription = await stream.SubscribeAsync(HandleEventAsync);
    }

    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Clean up subscription
        if (_subscription != null)
        {
            await _subscription.UnsubscribeAsync();
        }

        await base.OnDeactivateAsync(ct);
    }
}
```

## Comparison to Other Patterns

### Streams vs Direct Calls

| Feature | Streams | Direct Calls |
|---------|---------|--------------|
| Coupling | Loose | Tight |
| Fanout | Built-in | Manual |
| Persistence | Optional | N/A |
| Ordering | Per-key | Per-actor |
| Discovery | Dynamic | Static |

### Streams vs Message Queues

| Feature | Streams | Message Queues |
|---------|---------|----------------|
| Delivery | In-memory | Durable |
| Semantics | At-most-once | At-least-once |
| Ordering | Per-key | Per-queue |
| Backpressure | Not yet* | Built-in |
| Performance | ~1M ops/s | ~10K ops/s |

*Backpressure is planned for Phase 8

## Next Steps

- **[Clustering](Clustering)** - Distribute streams across multiple machines
- **[Examples](Examples)** - See the complete streaming example
- **[API Reference](API-Reference)** - Detailed stream API documentation

---

**Related**: [Actor Model](Actor-Model) | [Source Generators](Source-Generators) | [Persistence](Persistence)
