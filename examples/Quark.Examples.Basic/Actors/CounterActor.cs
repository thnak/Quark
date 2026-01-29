using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Basic.Actors;

/// <summary>
/// A simple counter actor that demonstrates basic actor functionality.
/// </summary>
[Actor(Name = "Counter", Reentrant = false)]
public class CounterActor : ActorBase
{
    private int _counter;

    public CounterActor(string actorId) : base(actorId)
    {
        _counter = 0;
    }

    public void Increment()
    {
        _counter++;
    }

    public int GetValue()
    {
        return _counter;
    }

    public async Task<string> ProcessMessageAsync(string message)
    {
        await Task.Delay(10); // Simulate some async work
        return $"Actor {ActorId} received: {message}";
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → CounterActor {ActorId} is being activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → CounterActor {ActorId} is being deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }
}