using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class TestActor : ActorBase
{
    public TestActor(string actorId) : base(actorId)
    {
    }

    public async Task<string> ProcessMessageAsync(string message)
    {
        await Task.Delay(1);
        return $"Processed: {message}";
    }
}