using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Placement;

[Actor(Name = "DataLoader")]
public class DataLoaderActor : ActorBase
{
    public DataLoaderActor(string actorId) : base(actorId) { }
    
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"DataLoaderActor {ActorId} activated");
        return Task.CompletedTask;
    }
}