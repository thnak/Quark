using Quark.Abstractions;
using Quark.Abstractions.Converters;
using Quark.Core.Actors;

namespace Quark.Tests;

public interface IMailboxTestActor : IQuarkActor
{
    [BinaryConverter(typeof(StringConverter))] // Return value
    Task<string> TestMethod();
}

[Actor(InterfaceType = typeof(IMailboxTestActor))]
public class MailboxTestActor : ActorBase, IMailboxTestActor
{
    public MailboxTestActor(string actorId) : base(actorId)
    {
    }

    public Task<string> TestMethod()
    {
        return Task.FromResult("test result");
    }
}