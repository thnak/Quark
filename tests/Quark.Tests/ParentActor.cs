using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class ParentActor : ActorBase
{
    public ParentActor(string actorId) : base(actorId)
    {
    }

    public ParentActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}