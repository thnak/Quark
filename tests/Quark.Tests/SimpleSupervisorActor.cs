using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Simple supervisor actor for testing.
/// </summary>
[Actor]
public class SimpleSupervisorActor : ActorBase, ISupervisor
{
    public SimpleSupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Simple strategy: always restart
        return Task.FromResult(SupervisionDirective.Restart);
    }
}