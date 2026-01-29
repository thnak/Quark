using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Supervision.Actors;

/// <summary>
/// A simple worker actor that can be supervised.
/// </summary>
[Actor(Name = "Worker", Reentrant = false)]
public class WorkerActor : ActorBase
{
    public WorkerActor(string actorId) : base(actorId)
    {
    }

    public WorkerActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public async Task<string> DoWorkAsync(string task)
    {
        await Task.Delay(10); // Simulate work
        return $"Worker {ActorId} completed: {task}";
    }
}