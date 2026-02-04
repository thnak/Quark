using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
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
        // Custom supervision: stop on InvalidOperationException, restart on others
        if (context.Exception is InvalidOperationException)
        {
            return Task.FromResult(SupervisionDirective.Stop);
        }

        return Task.FromResult(SupervisionDirective.Restart);
    }
}