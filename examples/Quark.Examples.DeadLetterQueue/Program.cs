using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.DeadLetterQueue;

/// <summary>
/// Example demonstrating Dead Letter Queue (DLQ) usage for capturing failed messages.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Dead Letter Queue Example ===\n");

        // Create a DLQ with capacity of 100 messages
        var dlq = new InMemoryDeadLetterQueue(maxMessages: 100);
        Console.WriteLine($"Created DLQ with capacity: 100");

        // Create an actor that will fail on certain inputs
        var actor = new CalculatorActor("calculator-1");
        Console.WriteLine($"Created actor: {actor.ActorId}");

        // Create mailbox with DLQ
        var mailbox = new ChannelMailbox(actor, capacity: 10, deadLetterQueue: dlq);
        await mailbox.StartAsync();
        Console.WriteLine("Mailbox started\n");

        // Send some messages - some will succeed, some will fail
        Console.WriteLine("Sending messages to actor...");
        
        // These will fail and go to DLQ (because InvokeMethodAsync is not implemented)
        await mailbox.PostAsync(new ActorMethodMessage<object>("Add", 5, 3));
        await mailbox.PostAsync(new ActorMethodMessage<object>("Divide", 10, 0)); // Division by zero
        await mailbox.PostAsync(new ActorMethodMessage<object>("Subtract", 20, 5));
        await mailbox.PostAsync(new ActorMethodMessage<object>("Multiply", 4, 5));

        // Wait for processing
        Console.WriteLine("Waiting for message processing...");
        await Task.Delay(2000);

        // Stop mailbox
        await mailbox.StopAsync();
        Console.WriteLine("Mailbox stopped\n");

        // Check DLQ
        Console.WriteLine($"Dead Letter Queue Status:");
        Console.WriteLine($"Total messages: {dlq.MessageCount}\n");

        if (dlq.MessageCount > 0)
        {
            Console.WriteLine("Failed Messages:");
            Console.WriteLine(new string('-', 80));

            var deadLetters = await dlq.GetAllAsync();
            foreach (var deadLetter in deadLetters)
            {
                if (deadLetter.Message is IActorMethodMessage<object> methodMsg)
                {
                    Console.WriteLine($"Message ID:    {deadLetter.Message.MessageId}");
                    Console.WriteLine($"Actor ID:      {deadLetter.ActorId}");
                    Console.WriteLine($"Method:        {methodMsg.MethodName}");
                    Console.WriteLine($"Arguments:     {string.Join(", ", methodMsg.Arguments ?? Array.Empty<object>())}");
                    Console.WriteLine($"Failed At:     {deadLetter.EnqueuedAt:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"Error Type:    {deadLetter.Exception.GetType().Name}");
                    Console.WriteLine($"Error Message: {deadLetter.Exception.Message}");
                    Console.WriteLine(new string('-', 80));
                }
            }

            // Demonstrate DLQ operations
            Console.WriteLine("\n=== DLQ Operations ===");
            
            // Get messages for specific actor
            var actorMessages = await dlq.GetByActorAsync("calculator-1");
            Console.WriteLine($"Messages for actor 'calculator-1': {actorMessages.Count}");

            // Remove a specific message
            if (deadLetters.Any())
            {
                var firstMessage = deadLetters.First();
                var removed = await dlq.RemoveAsync(firstMessage.Message.MessageId);
                Console.WriteLine($"Removed message {firstMessage.Message.MessageId}: {removed}");
                Console.WriteLine($"Remaining messages: {dlq.MessageCount}");
            }

            // Clear all messages
            await dlq.ClearAsync();
            Console.WriteLine($"Cleared DLQ. Remaining messages: {dlq.MessageCount}");
        }
        else
        {
            Console.WriteLine("No messages in Dead Letter Queue");
        }

        Console.WriteLine("\n=== Example Complete ===");
    }
}

/// <summary>
/// Example actor that performs calculations.
/// </summary>
[Actor(Name = "Calculator")]
public class CalculatorActor : ActorBase
{
    public CalculatorActor(string actorId) : base(actorId)
    {
    }

    public async Task<int> AddAsync(int a, int b)
    {
        await Task.CompletedTask;
        return a + b;
    }

    public async Task<int> SubtractAsync(int a, int b)
    {
        await Task.CompletedTask;
        return a - b;
    }

    public async Task<int> MultiplyAsync(int a, int b)
    {
        await Task.CompletedTask;
        return a * b;
    }

    public async Task<int> DivideAsync(int a, int b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("Cannot divide by zero");
        }
        await Task.CompletedTask;
        return a / b;
    }
}
