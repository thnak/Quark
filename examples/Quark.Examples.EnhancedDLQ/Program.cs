using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.EnhancedDLQ;

/// <summary>
/// Demonstrates the enhanced Dead Letter Queue features:
/// - Retry policies with exponential backoff
/// - Per-actor-type DLQ configuration
/// - Message replay functionality
/// - Actor-specific log scopes
/// - Log sampling for high-volume actors
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Enhanced DLQ & Logging Features Demo ===\n");

        await DemoRetryPolicyAsync();
        await DemoPerActorTypeDLQConfigAsync();
        await DemoMessageReplayAsync();
        await DemoLogSamplingAsync();

        Console.WriteLine("\n=== Demo Complete ===");
    }

    /// <summary>
    /// Demonstrates retry policies with exponential backoff.
    /// </summary>
    static async Task DemoRetryPolicyAsync()
    {
        Console.WriteLine("--- 1. Retry Policy with Exponential Backoff ---");

        // Create a retry policy
        var retryPolicy = new RetryPolicy
        {
            Enabled = true,
            MaxRetries = 3,
            InitialDelayMs = 100,
            MaxDelayMs = 5000,
            BackoffMultiplier = 2.0,
            UseJitter = true
        };

        Console.WriteLine($"Retry Policy: MaxRetries={retryPolicy.MaxRetries}, " +
                          $"InitialDelay={retryPolicy.InitialDelayMs}ms, " +
                          $"Multiplier={retryPolicy.BackoffMultiplier}");

        // Show calculated delays for each retry
        Console.WriteLine("\nCalculated retry delays:");
        for (int i = 1; i <= retryPolicy.MaxRetries; i++)
        {
            var delay = retryPolicy.CalculateDelay(i);
            Console.WriteLine($"  Retry {i}: {delay}ms");
        }

        // Test retry handler
        var handler = new RetryHandler(retryPolicy);
        var attemptCount = 0;

        Console.WriteLine("\nTesting retry with action that succeeds on 2nd attempt...");
        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            attemptCount++;
            Console.WriteLine($"  Attempt {attemptCount}");
            if (attemptCount < 2)
                throw new InvalidOperationException($"Simulated failure on attempt {attemptCount}");
            await Task.CompletedTask;
        });

        Console.WriteLine($"Result: Success={result.Success}, RetryCount={result.RetryCount}");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates per-actor-type DLQ configuration.
    /// </summary>
    static async Task DemoPerActorTypeDLQConfigAsync()
    {
        Console.WriteLine("--- 2. Per-Actor-Type DLQ Configuration ---");

        // Create global DLQ options with actor-specific overrides
        var dlqOptions = new DeadLetterQueueOptions
        {
            Enabled = true,
            MaxMessages = 1000,
            CaptureStackTraces = true,
            GlobalRetryPolicy = new RetryPolicy { MaxRetries = 2 }
        };

        // Configure specific settings for critical actors
        dlqOptions.ActorTypeConfigurations["PaymentProcessor"] = new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "PaymentProcessor",
            Enabled = true,
            MaxMessages = 5000, // Higher limit for critical actors
            CaptureStackTraces = true,
            RetryPolicy = new RetryPolicy 
            { 
                MaxRetries = 5, // More retries for critical operations
                InitialDelayMs = 200 
            }
        };

        // Configure for high-volume actors
        dlqOptions.ActorTypeConfigurations["LogProcessor"] = new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "LogProcessor",
            Enabled = true,
            MaxMessages = 500, // Lower limit for high-volume actors
            CaptureStackTraces = false, // Save memory
            RetryPolicy = new RetryPolicy { MaxRetries = 1 } // Fewer retries
        };

        // Show effective configurations
        Console.WriteLine("Global DLQ Settings:");
        Console.WriteLine($"  MaxMessages: {dlqOptions.MaxMessages}");
        Console.WriteLine($"  Global MaxRetries: {dlqOptions.GlobalRetryPolicy?.MaxRetries ?? 0}");

        Console.WriteLine("\nPaymentProcessor (Critical Actor) Settings:");
        var (enabled1, maxMsg1, capture1, retry1) = dlqOptions.GetEffectiveConfiguration("PaymentProcessor");
        Console.WriteLine($"  Enabled: {enabled1}, MaxMessages: {maxMsg1}");
        Console.WriteLine($"  CaptureStackTraces: {capture1}, MaxRetries: {retry1?.MaxRetries ?? 0}");

        Console.WriteLine("\nLogProcessor (High-Volume Actor) Settings:");
        var (enabled2, maxMsg2, capture2, retry2) = dlqOptions.GetEffectiveConfiguration("LogProcessor");
        Console.WriteLine($"  Enabled: {enabled2}, MaxMessages: {maxMsg2}");
        Console.WriteLine($"  CaptureStackTraces: {capture2}, MaxRetries: {retry2?.MaxRetries ?? 0}");

        Console.WriteLine("\nUnknownActor (Uses Global Settings):");
        var (enabled3, maxMsg3, capture3, retry3) = dlqOptions.GetEffectiveConfiguration("UnknownActor");
        Console.WriteLine($"  Enabled: {enabled3}, MaxMessages: {maxMsg3}");
        Console.WriteLine($"  CaptureStackTraces: {capture3}, MaxRetries: {retry3?.MaxRetries ?? 0}");
        
        Console.WriteLine();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Demonstrates DLQ message replay functionality.
    /// </summary>
    static async Task DemoMessageReplayAsync()
    {
        Console.WriteLine("--- 3. Message Replay Functionality ---");

        // Create DLQ and actor
        var dlq = new InMemoryDeadLetterQueue(maxMessages: 100);
        var actor = new TestActor("test-actor-1");
        var mailbox = new ChannelMailbox(actor, capacity: 100, deadLetterQueue: dlq);

        Console.WriteLine("Creating test messages and simulating failures...");
        
        // Simulate failed messages
        var message1 = new ActorMethodMessage<string>("ProcessOrder", "order-1");
        var message2 = new ActorMethodMessage<string>("ProcessOrder", "order-2");
        var message3 = new ActorMethodMessage<string>("ProcessOrder", "order-3");

        await dlq.EnqueueAsync(message1, "test-actor-1", new InvalidOperationException("Database timeout"));
        await dlq.EnqueueAsync(message2, "test-actor-1", new InvalidOperationException("Network error"));
        await dlq.EnqueueAsync(message3, "test-actor-2", new InvalidOperationException("Validation error"));

        Console.WriteLine($"DLQ contains {dlq.MessageCount} failed messages");

        // Show messages by actor
        var actor1Messages = await dlq.GetByActorAsync("test-actor-1");
        Console.WriteLine($"\nMessages for test-actor-1: {actor1Messages.Count}");
        foreach (var msg in actor1Messages)
        {
            Console.WriteLine($"  - {msg.Message.MessageId}: {msg.Exception.Message}");
        }

        // Replay single message
        Console.WriteLine("\nReplaying single message...");
        IMailbox? MailboxProvider(string actorId) => actorId == "test-actor-1" ? mailbox : null;
        
        var replayed = await dlq.ReplayAsync(message1.MessageId, MailboxProvider);
        Console.WriteLine($"Single replay result: {replayed}");
        Console.WriteLine($"DLQ now contains {dlq.MessageCount} messages");

        // Replay all messages for an actor
        Console.WriteLine("\nReplaying all messages for test-actor-1...");
        var replayedIds = await dlq.ReplayByActorAsync("test-actor-1", MailboxProvider);
        Console.WriteLine($"Replayed {replayedIds.Count} messages");
        Console.WriteLine($"DLQ now contains {dlq.MessageCount} messages (only test-actor-2 remains)");

        mailbox.Dispose();
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates log sampling configuration for high-volume actors.
    /// </summary>
    static async Task DemoLogSamplingAsync()
    {
        Console.WriteLine("--- 4. Log Sampling for High-Volume Actors ---");

        // Create logging options with global and per-actor sampling
        var loggingOptions = new ActorLoggingOptions
        {
            UseActorScopes = true,
            GlobalSamplingConfiguration = new LogSamplingConfiguration
            {
                Enabled = true,
                SamplingRate = 0.1, // Log 10% of messages
                MinimumLevelForSampling = 2, // Information and above
                AlwaysLogErrors = true
            }
        };

        // High-volume actor: more aggressive sampling
        loggingOptions.ActorTypeSamplingConfigurations["HighVolumeProcessor"] = new LogSamplingConfiguration
        {
            Enabled = true,
            SamplingRate = 0.01, // Log only 1% of messages
            MinimumLevelForSampling = 2,
            AlwaysLogErrors = true
        };

        // Critical actor: no sampling
        loggingOptions.ActorTypeSamplingConfigurations["CriticalPaymentProcessor"] = new LogSamplingConfiguration
        {
            Enabled = false // Log everything
        };

        Console.WriteLine("Global Sampling Configuration:");
        var globalConfig = loggingOptions.GlobalSamplingConfiguration;
        Console.WriteLine($"  SamplingRate: {globalConfig?.SamplingRate:P0}");
        Console.WriteLine($"  AlwaysLogErrors: {globalConfig?.AlwaysLogErrors}");

        Console.WriteLine("\nHigh-Volume Actor Sampling:");
        var hvConfig = loggingOptions.GetSamplingConfiguration("HighVolumeProcessor");
        Console.WriteLine($"  SamplingRate: {hvConfig?.SamplingRate:P2}");
        
        Console.WriteLine("\nCritical Actor Sampling:");
        var criticalConfig = loggingOptions.GetSamplingConfiguration("CriticalPaymentProcessor");
        Console.WriteLine($"  Enabled: {criticalConfig?.Enabled}");
        Console.WriteLine($"  (Logs everything when disabled)");

        // Demonstrate sampling in action
        Console.WriteLine("\nSimulating 100 log entries with 10% sampling:");
        var loggedCount = 0;
        var samplingConfig = new LogSamplingConfiguration { SamplingRate = 0.1 };
        
        for (int i = 0; i < 100; i++)
        {
            if (samplingConfig.ShouldLog())
                loggedCount++;
        }
        
        Console.WriteLine($"  Logged {loggedCount} out of 100 entries (~10%)");
        
        Console.WriteLine();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Simple test actor for demonstration.
/// </summary>
[Actor(Name = "TestActor")]
public class TestActor : ActorBase
{
    public TestActor(string actorId) : base(actorId)
    {
    }

    public Task<string> ProcessOrder(string orderId)
    {
        return Task.FromResult($"Processed order: {orderId}");
    }
}
