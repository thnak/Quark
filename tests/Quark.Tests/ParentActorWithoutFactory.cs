using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class ParentActorWithoutFactory : ActorBase
{
    public ParentActorWithoutFactory(string actorId) : base(actorId)
    {
    }
}