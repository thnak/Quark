using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;

namespace Quark.Examples.Streaming;

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