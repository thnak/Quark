using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Supervision.Actors;

/// <summary>
/// A supervisor actor that uses the default supervision strategy (Restart).
/// </summary>
[Actor(Name = "Supervisor", Reentrant = false)]
public class SupervisorActor : ActorBase
{
    public SupervisorActor(string actorId) : base(actorId)
    {
    }

    public SupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → SupervisorActor {ActorId} is being activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → SupervisorActor {ActorId} is being deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }
}