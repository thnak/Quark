using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor(Name = "TestStateless", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class TestStatelessActor : StatelessActorBase
{
    public TestStatelessActor(string actorId) : base(actorId)
    {
    }

    public TestStatelessActor(string actorId, IActorFactory? actorFactory) : base(actorId, actorFactory)
    {
    }

    public async Task<string> ProcessMessageAsync(string message)
    {
        await Task.Delay(1);
        return $"Processed: {message}";
    }
}