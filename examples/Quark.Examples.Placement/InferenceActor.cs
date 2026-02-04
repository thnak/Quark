using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Placement.Abstractions;

namespace Quark.Examples.Placement;

[Actor(Name = "Inference")]
[GpuBound]
public class InferenceActor : ActorBase
{
    public InferenceActor(string actorId) : base(actorId) { }
    
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"InferenceActor {ActorId} activated (GPU-accelerated)");
        return Task.CompletedTask;
    }
}