using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Simple worker actor for testing.
/// </summary>
[Actor]
public class SimpleWorkerActor : ActorBase
{
    public SimpleWorkerActor(string actorId) : base(actorId)
    {
    }

    public Task<int> DoWorkAsync()
    {
        return Task.FromResult(42);
    }
}