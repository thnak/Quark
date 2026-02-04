using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;

namespace Quark.Examples.Streaming;

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