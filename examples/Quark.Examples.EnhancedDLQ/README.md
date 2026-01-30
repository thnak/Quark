# Enhanced Dead Letter Queue and Logging Features Example

This example demonstrates the enhanced Dead Letter Queue (DLQ) and logging features added to Quark:

## Features Demonstrated

### 1. Retry Policies with Exponential Backoff

Configure retry behavior for failed messages before they are sent to the DLQ:

```csharp
var retryPolicy = new RetryPolicy
{
    Enabled = true,
    MaxRetries = 3,
    InitialDelayMs = 100,
    MaxDelayMs = 5000,
    BackoffMultiplier = 2.0,
    UseJitter = true  // Randomize delays to prevent thundering herd
};
```

Features:
- Exponential backoff with configurable multiplier
- Maximum delay cap to prevent excessive waiting
- Optional jitter to prevent synchronized retries
- Automatic delay calculation based on attempt number

### 2. Per-Actor-Type DLQ Configuration

Different actor types can have different DLQ settings:

```csharp
var dlqOptions = new DeadLetterQueueOptions
{
    // Global defaults
    MaxMessages = 1000,
    GlobalRetryPolicy = new RetryPolicy { MaxRetries = 2 },
    
    // Per-actor-type overrides
    ActorTypeConfigurations = new Dictionary<string, ActorTypeDeadLetterQueueOptions>
    {
        ["PaymentProcessor"] = new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "PaymentProcessor",
            MaxMessages = 5000,  // Higher limit for critical actors
            RetryPolicy = new RetryPolicy { MaxRetries = 5 }
        }
    }
};
```

Benefits:
- Critical actors can have more retries and larger DLQ capacity
- High-volume actors can have reduced capacity to save memory
- Per-actor stack trace capture configuration

### 3. Message Replay Functionality

Failed messages can be replayed from the DLQ:

```csharp
// Replay a single message
var success = await dlq.ReplayAsync(messageId, mailboxProvider);

// Replay multiple messages
var replayedIds = await dlq.ReplayBatchAsync(messageIds, mailboxProvider);

// Replay all messages for an actor
var replayedIds = await dlq.ReplayByActorAsync(actorId, mailboxProvider);
```

Use cases:
- Recover from transient failures after the root cause is fixed
- Manual intervention for critical failed operations
- Batch replay during maintenance windows
- Actor-specific replay when individual actors recover

### 4. Log Sampling for High-Volume Actors

Reduce log volume for high-frequency actors while maintaining visibility:

```csharp
var loggingOptions = new ActorLoggingOptions
{
    UseActorScopes = true,
    GlobalSamplingConfiguration = new LogSamplingConfiguration
    {
        SamplingRate = 0.1,  // Log 10% of messages
        AlwaysLogErrors = true  // Never sample errors
    },
    
    // Per-actor-type sampling
    ActorTypeSamplingConfigurations = new Dictionary<string, LogSamplingConfiguration>
    {
        ["HighVolumeActor"] = new LogSamplingConfiguration
        {
            SamplingRate = 0.01  // Log only 1%
        }
    }
};
```

Features:
- Configurable sampling rates per actor type
- Always log errors and critical messages
- Minimum log level threshold for sampling
- Actor-specific log scopes for correlation

## Running the Example

```bash
# Build the example
dotnet build examples/Quark.Examples.EnhancedDLQ

# Run the example
dotnet run --project examples/Quark.Examples.EnhancedDLQ
```

## Expected Output

The example will demonstrate:

1. **Retry Policy**: Shows calculated delays with jitter and successful retry after failure
2. **Per-Actor Config**: Displays effective configurations for different actor types
3. **Message Replay**: Demonstrates replaying individual and batch messages
4. **Log Sampling**: Shows sampling behavior with different rates

## Key Takeaways

1. **Resilience**: Retry policies make your system more resilient to transient failures
2. **Configurability**: Per-actor-type configuration allows fine-tuned behavior
3. **Observability**: Log sampling maintains visibility while controlling volume
4. **Recovery**: Message replay enables recovery from failures

## Integration with Your Application

To use these features in your application:

1. Configure `DeadLetterQueueOptions` with retry policies
2. Pass configured DLQ to mailbox constructor
3. Set up `ActorLoggingOptions` for sampling
4. Implement replay logic using the DLQ interface

See the main Quark documentation for more details.
