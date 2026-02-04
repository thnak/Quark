using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class ChildActor : ActorBase
{
    public ChildActor(string actorId) : base(actorId)
    {
    }

    public ChildActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}