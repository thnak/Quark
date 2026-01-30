# Dead Letter Queue (DLQ) Usage Guide

The Dead Letter Queue feature in Quark captures failed actor messages for analysis and debugging, helping you understand and resolve issues in production systems.

## Overview

When an actor fails to process a message (throws an exception), the message can be automatically captured in a Dead Letter Queue along with:
- The original message
- The actor ID that failed
- The exception that caused the failure
- Timestamp of the failure
- Retry count (if applicable)

## Basic Usage

### 1. Create a DLQ Instance

```csharp
using Quark.Core.Actors;

// Create an in-memory DLQ with default capacity (10,000 messages)
var dlq = new InMemoryDeadLetterQueue();

// Or specify a custom capacity
var dlq = new InMemoryDeadLetterQueue(maxMessages: 5000);
```

### 2. Integrate with Mailbox

```csharp
using Quark.Core.Actors;

// Create an actor
var actor = new MyActor("actor-1");

// Create a mailbox with DLQ
var mailbox = new ChannelMailbox(actor, capacity: 1000, deadLetterQueue: dlq);

// Start processing
await mailbox.StartAsync();
```

### 3. Query Failed Messages

```csharp
// Get all dead letter messages
var allMessages = await dlq.GetAllAsync();

// Get messages for a specific actor
var actorMessages = await dlq.GetByActorAsync("actor-1");

// Check DLQ size
int count = dlq.MessageCount;
```

### 4. Manage DLQ

```csharp
// Remove a specific message
bool removed = await dlq.RemoveAsync(messageId);

// Clear all messages
await dlq.ClearAsync();
```

## Configuration Options

```csharp
using Quark.Abstractions;

var options = new DeadLetterQueueOptions
{
    Enabled = true,                 // Enable/disable DLQ
    MaxMessages = 10000,            // Maximum messages to retain
    CaptureStackTraces = true       // Capture full stack traces
};
```

## Diagnostic Endpoints

Quark provides HTTP endpoints for DLQ inspection:

### View Dead Letter Messages

```bash
# Get all messages
GET /quark/dlq

# Filter by actor
GET /quark/dlq?actorId=my-actor-1
```

Response:
```json
{
  "totalMessages": 5,
  "filteredCount": 2,
  "messages": [
    {
      "messageId": "abc-123",
      "actorId": "my-actor-1",
      "enqueuedAt": "2026-01-30T00:00:00Z",
      "retryCount": 0,
      "errorType": "InvalidOperationException",
      "errorMessage": "Something went wrong",
      "correlationId": "trace-xyz"
    }
  ]
}
```

### Clear Dead Letter Queue

```bash
# Clear all messages
DELETE /quark/dlq

# Remove specific message
DELETE /quark/dlq/{messageId}
```

## Example: Actor with DLQ

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor]
public class PaymentProcessor : ActorBase
{
    public PaymentProcessor(string actorId) : base(actorId) { }

    public async Task<bool> ProcessPaymentAsync(decimal amount)
    {
        if (amount <= 0)
        {
            // This exception will be captured in DLQ
            throw new InvalidOperationException($"Invalid amount: {amount}");
        }

        // Process payment
        await Task.Delay(100);
        return true;
    }
}

// Setup
var dlq = new InMemoryDeadLetterQueue();
var actor = new PaymentProcessor("payment-1");
var mailbox = new ChannelMailbox(actor, deadLetterQueue: dlq);

await mailbox.StartAsync();

// This will fail and go to DLQ
var failedMessage = new ActorMethodMessage<bool>("ProcessPaymentAsync", -10);
await mailbox.PostAsync(failedMessage);

// Wait and check DLQ
await Task.Delay(500);
var failures = await dlq.GetAllAsync();
// failures.Count == 1
```

## Best Practices

### 1. Set Appropriate Capacity

```csharp
// For high-volume systems, set a reasonable limit
var dlq = new InMemoryDeadLetterQueue(maxMessages: 50000);
```

### 2. Monitor DLQ Size

```csharp
// Periodically check DLQ size
if (dlq.MessageCount > 1000)
{
    logger.LogWarning($"DLQ has {dlq.MessageCount} messages");
}
```

### 3. Regular Cleanup

```csharp
// Clear old messages periodically
await dlq.ClearAsync();
```

### 4. Analyze Patterns

```csharp
// Group failures by actor type
var failures = await dlq.GetAllAsync();
var byActor = failures.GroupBy(m => m.ActorId);

foreach (var group in byActor)
{
    Console.WriteLine($"Actor {group.Key}: {group.Count()} failures");
}
```

## Limitations

### Current Limitations

- **In-Memory Only**: The `InMemoryDeadLetterQueue` stores messages in memory. For distributed scenarios, consider implementing persistent storage (Redis, database, etc.)
- **No Automatic Retry**: Messages are captured but not automatically retried. Manual replay logic is needed.
- **Capacity Limits**: When max capacity is reached, oldest messages are removed (FIFO).

### Future Enhancements

- Persistent DLQ implementations (Redis, database)
- Automatic retry with exponential backoff
- DLQ message replay functionality
- Per-actor-type DLQ configuration
- Dead letter message expiration

## Implementing Custom DLQ

You can implement custom DLQ storage:

```csharp
using Quark.Abstractions;

public class RedisDLQ : IDeadLetterQueue
{
    private readonly IConnectionMultiplexer _redis;

    public RedisDLQ(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public int MessageCount => /* query Redis */;

    public async Task EnqueueAsync(
        IActorMessage message, 
        string actorId, 
        Exception exception, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(new DeadLetterMessage
        {
            Message = message,
            ActorId = actorId,
            Exception = exception,
            EnqueuedAt = DateTimeOffset.UtcNow
        });
        await db.ListLeftPushAsync("dlq", json);
    }

    // Implement other methods...
}
```

## Troubleshooting

### DLQ Not Capturing Messages

1. Verify DLQ is passed to mailbox constructor
2. Check that exceptions are being thrown by actor methods
3. Ensure mailbox is started with `StartAsync()`

### High Memory Usage

1. Reduce `maxMessages` capacity
2. Implement periodic cleanup
3. Consider persistent storage implementation

### Messages Disappearing

- Check if capacity is reached (oldest messages removed)
- Verify no other code is calling `ClearAsync()`

## See Also

- [ENHANCEMENTS.md](./ENHANCEMENTS.md) - Feature roadmap
- [ChannelMailbox Documentation](../src/Quark.Core.Actors/ChannelMailbox.cs)
- [Health Monitoring Guide](./ENHANCEMENTS.md#72-health-monitoring--diagnostics)
