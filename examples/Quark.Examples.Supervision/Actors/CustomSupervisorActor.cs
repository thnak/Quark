using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Supervision.Actors;

/// <summary>
/// A custom supervisor actor with specific supervision strategies.
/// </summary>
[Actor(Name = "CustomSupervisor", Reentrant = false)]
public class CustomSupervisorActor : ActorBase
{
    public CustomSupervisorActor(string actorId) : base(actorId)
    {
    }

    public CustomSupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Custom supervision logic based on exception type
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Escalate),
            _ => Task.FromResult(SupervisionDirective.Restart)
        };
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → CustomSupervisorActor {ActorId} is being activated");
        return base.OnActivateAsync(cancellationToken);
    }
}