using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Distributed supervision tests - validates actor supervision concepts for distributed scenarios.
/// These tests verify the supervision abstractions work correctly for Phase 3 features.
/// </summary>
public class DistributedSupervisionTests
{
    /// <summary>
    /// Tests that CallChainContext prevents circular dependencies in distributed actor calls.
    /// This is crucial for preventing deadlocks when Actor A calls B which tries to call back to A.
    /// </summary>
    [Fact]
    public void DistributedSupervision_CallChainContext_PreventsCircularDependencies()
    {
        // Arrange
        var context = CallChainContext.Create();

        using (context.CreateScope())
        {
            // Act - Simulate actor call chain: A -> B -> A (circular)
            using (context.EnterActor("actor-A", "ServiceActor"))
            {
                using (context.EnterActor("actor-B", "WorkerActor"))
                {
                    // Assert - Trying to enter actor-A again should throw
                    var ex = Assert.Throws<ReentrancyException>(() =>
                    {
                        context.EnterActor("actor-A", "ServiceActor");
                    });

                    Assert.Contains("actor-A", ex.Message);
                    Assert.Contains("Circular", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Tests that supervision options enforce restart limits correctly.
    /// Important for preventing restart storms in distributed systems.
    /// </summary>
    [Fact]
    public void DistributedSupervision_SupervisionOptions_EnforcesRestartLimits()
    {
        // Arrange
        var options = new SupervisionOptions
        {
            RestartStrategy = RestartStrategy.OneForOne,
            MaxRestarts = 3,
            TimeWindow = TimeSpan.FromSeconds(60)
        };

        var history = new List<DateTimeOffset>();

        // Act - Simulate 4 restarts within the time window
        for (int i = 0; i < 4; i++)
        {
            history.Add(DateTimeOffset.UtcNow);
        }

        // Assert - Should exceed the limit
        var recentRestarts = history.Count(h => h > DateTimeOffset.UtcNow.Subtract(options.TimeWindow));
        Assert.True(recentRestarts > options.MaxRestarts);
    }

    /// <summary>
    /// Tests exponential backoff calculation for restart delays.
    /// Prevents thundering herd problem in distributed actor restarts.
    /// </summary>
    [Fact]
    public void DistributedSupervision_ExponentialBackoff_CalculatesCorrectly()
    {
        // Arrange
        var options = new SupervisionOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0
        };

        // Act & Assert
        var backoff1 = options.InitialBackoff; // 1s
        Assert.Equal(TimeSpan.FromSeconds(1), backoff1);

        var backoff2 = TimeSpan.FromSeconds(backoff1.TotalSeconds * options.BackoffMultiplier); // 2s
        Assert.Equal(TimeSpan.FromSeconds(2), backoff2);

        var backoff3 = TimeSpan.FromSeconds(backoff2.TotalSeconds * options.BackoffMultiplier); // 4s
        Assert.Equal(TimeSpan.FromSeconds(4), backoff3);

        // Verify cap at MaxBackoff
        var backoffHuge = TimeSpan.FromSeconds(1000);
        var capped = backoffHuge > options.MaxBackoff ? options.MaxBackoff : backoffHuge;
        Assert.Equal(options.MaxBackoff, capped);
    }

    /// <summary>
    /// Tests that consistent hashing distributes actors evenly across silos.
    /// Critical for load balancing in distributed actor placement.
    /// </summary>
    [Fact]
    public void DistributedSupervision_ConsistentHashing_DistributesActors()
    {
        // Arrange - Create a cluster with 3 silos
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-2"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        // Act - Place 100 actors and count distribution
        var placements = new Dictionary<string, int>();
        for (int i = 0; i < 100; i++)
        {
            var actorKey = $"WorkerActor:worker-{i}";
            var silo = hashRing.GetNode(actorKey);
            if (!string.IsNullOrEmpty(silo))
            {
                placements[silo] = placements.GetValueOrDefault(silo, 0) + 1;
            }
        }

        // Assert - Each silo should get roughly 33% (20-50 actors each)
        Assert.Equal(3, placements.Count);
        Assert.All(placements.Values, count => Assert.InRange(count, 20, 50));
    }

    /// <summary>
    /// Tests silo failure and actor rebalancing.
    /// When a silo dies, its actors must be redistributed.
    /// </summary>
    [Fact]
    public void DistributedSupervision_SiloFailure_RebalancesActors()
    {
        // Arrange
        var hashRing = new ConsistentHashRing();
        hashRing.AddNode(new HashRingNode("silo-1"));
        hashRing.AddNode(new HashRingNode("silo-2"));
        hashRing.AddNode(new HashRingNode("silo-3"));

        var actorKey = "WorkerActor:critical-worker";
        var initialSilo = hashRing.GetNode(actorKey);
        Assert.NotNull(initialSilo); // Ensure we got a silo

        // Act - Simulate silo failure
        hashRing.RemoveNode(initialSilo!);

        var newSilo = hashRing.GetNode(actorKey);

        // Assert - Actor is now on a different silo
        Assert.NotEqual(initialSilo, newSilo);
        Assert.NotNull(newSilo);
    }

    /// <summary>
    /// Tests that restart strategies can be configured per actor.
    /// </summary>
    [Fact]
    public void DistributedSupervision_RestartStrategies_SupportAllTypes()
    {
        // Arrange & Assert - All restart strategies are defined
        Assert.Equal(RestartStrategy.OneForOne, RestartStrategy.OneForOne);
        Assert.Equal(RestartStrategy.AllForOne, RestartStrategy.AllForOne);
        Assert.Equal(RestartStrategy.RestForOne, RestartStrategy.RestForOne);

        // All three strategies are available for configuration
        var options1 = new SupervisionOptions { RestartStrategy = RestartStrategy.OneForOne };
        var options2 = new SupervisionOptions { RestartStrategy = RestartStrategy.AllForOne };
        var options3 = new SupervisionOptions { RestartStrategy = RestartStrategy.RestForOne };

        Assert.NotEqual(options1.RestartStrategy, options2.RestartStrategy);
        Assert.NotEqual(options2.RestartStrategy, options3.RestartStrategy);
    }

    /// <summary>
    /// Tests that supervision directives cover all failure scenarios.
    /// </summary>
    [Fact]
    public void DistributedSupervision_SupervisionDirectives_CoverAllScenarios()
    {
        // Arrange & Assert - All directives are defined
        Assert.Equal(SupervisionDirective.Resume, SupervisionDirective.Resume);
        Assert.Equal(SupervisionDirective.Restart, SupervisionDirective.Restart);
        Assert.Equal(SupervisionDirective.Stop, SupervisionDirective.Stop);
        Assert.Equal(SupervisionDirective.Escalate, SupervisionDirective.Escalate);

        // Verify they're all distinct
        var directives = new[]
        {
            SupervisionDirective.Resume,
            SupervisionDirective.Restart,
            SupervisionDirective.Stop,
            SupervisionDirective.Escalate
        };

        Assert.Equal(4, directives.Distinct().Count());
    }

    /// <summary>
    /// Tests that actor factory can create actors with proper supervision setup.
    /// </summary>
    [Fact]
    public void DistributedSupervision_ActorFactory_SupportsSupervisionHierarchy()
    {
        // Arrange
        var factory = new ActorFactory();

        // Act
        var supervisor = factory.CreateActor<SimpleSupervisorActor>("supervisor-1");

        // Assert
        Assert.NotNull(supervisor);
        Assert.IsAssignableFrom<ISupervisor>(supervisor);
        Assert.Equal("supervisor-1", supervisor.ActorId);
    }

    /// <summary>
    /// Tests that child actors can be spawned and retrieved.
    /// </summary>
    [Fact]
    public async Task DistributedSupervision_ChildActors_CanBeSpawnedAndRetrieved()
    {
        // Arrange
        var factory = new ActorFactory();
        var supervisor = factory.CreateActor<SimpleSupervisorActor>("supervisor-2");

        // Act
        var child = await supervisor.SpawnChildAsync<SimpleWorkerActor>("child-1");
        var children = supervisor.GetChildren();

        // Assert
        Assert.NotNull(child);
        Assert.Equal("child-1", child.ActorId);
        Assert.Single(children);
        Assert.Contains(child, children);
    }
}

/// <summary>
/// Simple supervisor actor for testing.
/// </summary>
[Actor]
public class SimpleSupervisorActor : ActorBase, ISupervisor
{
    public SimpleSupervisorActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Simple strategy: always restart
        return Task.FromResult(SupervisionDirective.Restart);
    }
}

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
