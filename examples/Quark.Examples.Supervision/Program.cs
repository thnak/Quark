using Quark.Abstractions;
using Quark.Core.Actors;

Console.WriteLine("=== Quark Actor Framework - Supervision Example ===");
Console.WriteLine();

// Create an actor factory
var factory = new ActorFactory();
Console.WriteLine("✓ Actor factory created");

// Create a supervisor actor
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");
Console.WriteLine($"✓ Supervisor actor created with ID: {supervisor.ActorId}");

// Activate the supervisor
await supervisor.OnActivateAsync();
Console.WriteLine("✓ Supervisor activated");

// Spawn child actors
var child1 = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
Console.WriteLine($"✓ Spawned child actor: {child1.ActorId}");

var child2 = await supervisor.SpawnChildAsync<WorkerActor>("worker-2");
Console.WriteLine($"✓ Spawned child actor: {child2.ActorId}");

var child3 = await supervisor.SpawnChildAsync<WorkerActor>("worker-3");
Console.WriteLine($"✓ Spawned child actor: {child3.ActorId}");

// Get all children
var children = supervisor.GetChildren();
Console.WriteLine($"✓ Supervisor has {children.Count} children");
foreach (var child in children)
{
    Console.WriteLine($"  → Child: {child.ActorId}");
}

Console.WriteLine();
Console.WriteLine("--- Testing Supervision ---");

// Simulate a child failure and test supervision
var failedChild = children.First();
var exception = new InvalidOperationException("Simulated worker failure");
var failureContext = new ChildFailureContext(failedChild, exception);

Console.WriteLine($"✓ Simulating failure in child: {failedChild.ActorId}");
Console.WriteLine($"  Exception: {exception.Message}");

var directive = await supervisor.OnChildFailureAsync(failureContext);
Console.WriteLine($"✓ Supervision directive: {directive}");

// Test different supervision strategies
Console.WriteLine();
Console.WriteLine("--- Testing Custom Supervision Strategies ---");

var customSupervisor = factory.CreateActor<CustomSupervisorActor>("custom-supervisor");
await customSupervisor.OnActivateAsync();

var worker = await customSupervisor.SpawnChildAsync<WorkerActor>("worker-custom");
Console.WriteLine($"✓ Spawned worker under custom supervisor: {worker.ActorId}");

// Test with retriable exception (should Resume)
var retriableException = new TimeoutException("Temporary timeout");
var retriableContext = new ChildFailureContext(worker, retriableException);
var retriableDirective = await customSupervisor.OnChildFailureAsync(retriableContext);
Console.WriteLine($"✓ TimeoutException → Directive: {retriableDirective}");

// Test with fatal exception (should Stop)
var fatalException = new OutOfMemoryException("Critical error");
var fatalContext = new ChildFailureContext(worker, fatalException);
var fatalDirective = await customSupervisor.OnChildFailureAsync(fatalContext);
Console.WriteLine($"✓ OutOfMemoryException → Directive: {fatalDirective}");

// Test with unknown exception (should Restart)
var unknownException = new Exception("Unknown error");
var unknownContext = new ChildFailureContext(worker, unknownException);
var unknownDirective = await customSupervisor.OnChildFailureAsync(unknownContext);
Console.WriteLine($"✓ Unknown Exception → Directive: {unknownDirective}");

// Deactivate actors
await customSupervisor.OnDeactivateAsync();
await supervisor.OnDeactivateAsync();
Console.WriteLine();
Console.WriteLine("✓ All actors deactivated");

Console.WriteLine();
Console.WriteLine("=== Example completed successfully ===");

/// <summary>
/// A supervisor actor that uses the default supervision strategy (Restart).
/// </summary>
[Actor(Name = "Supervisor", Reentrant = false)]
public class SupervisorActor : ActorBase
{
    public SupervisorActor(string actorId) : base(actorId)
    {
    }

    public SupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → SupervisorActor {ActorId} is being activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → SupervisorActor {ActorId} is being deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }
}

/// <summary>
/// A custom supervisor actor with specific supervision strategies.
/// </summary>
[Actor(Name = "CustomSupervisor", Reentrant = false)]
public class CustomSupervisorActor : ActorBase
{
    public CustomSupervisorActor(string actorId) : base(actorId)
    {
    }

    public CustomSupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Custom supervision logic based on exception type
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Escalate),
            _ => Task.FromResult(SupervisionDirective.Restart)
        };
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  → CustomSupervisorActor {ActorId} is being activated");
        return base.OnActivateAsync(cancellationToken);
    }
}

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
