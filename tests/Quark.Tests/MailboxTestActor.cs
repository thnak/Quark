using Quark.Core.Actors;

namespace Quark.Tests;

[Quark.Abstractions.Actor]
public class MailboxTestActor : ActorBase
{
    public MailboxTestActor(string actorId) : base(actorId)
    {
    }

    public Task<string> TestMethod()
    {
        return Task.FromResult("test result");
    }
}