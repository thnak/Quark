using Quark.Abstractions;
using Quark.Core.Actors;

Console.WriteLine("=== Quark Actor Framework - Basic Example ===");
Console.WriteLine();

// Create an actor factory
var factory = new ActorFactory();
Console.WriteLine("✓ Actor factory created");

// Create a counter actor
var counter = factory.CreateActor<CounterActor>("counter-1");
Console.WriteLine($"✓ Counter actor created with ID: {counter.ActorId}");

// Activate the actor
await counter.OnActivateAsync();
Console.WriteLine("✓ Actor activated");

// Increment the counter
counter.Increment();
Console.WriteLine($"✓ Counter incremented to: {counter.GetValue()}");

counter.Increment();
counter.Increment();
Console.WriteLine($"✓ Counter incremented to: {counter.GetValue()}");

// Process a message
var message = await counter.ProcessMessageAsync("Hello from Quark!");
Console.WriteLine($"✓ Message processed: {message}");

// Get or create the same actor (should return the same instance)
var sameCounter = factory.GetOrCreateActor<CounterActor>("counter-1");
Console.WriteLine($"✓ GetOrCreate returned same instance: {ReferenceEquals(counter, sameCounter)}");

// Deactivate the actor
await counter.OnDeactivateAsync();
Console.WriteLine("✓ Actor deactivated");

Console.WriteLine();
Console.WriteLine("=== Example completed successfully ===");

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

