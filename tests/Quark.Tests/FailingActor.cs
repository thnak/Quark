using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class FailingActor : ActorBase
{
    public FailingActor(string actorId) : base(actorId)
    {
    }

    public Task<string> ThrowError(string errorMessage)
    {
        throw new InvalidOperationException(errorMessage);
    }

    public Task<string> SuccessMethod(string input)
    {
        return Task.FromResult($"Success: {input}");
    }
}