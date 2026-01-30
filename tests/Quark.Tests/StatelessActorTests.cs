using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

public class StatelessActorTests
{
    [Fact]
    public void Constructor_WithValidId_CreatesActor()
    {
        // Arrange
        var actorId = "stateless-worker-1";

        // Act
        var actor = new TestStatelessActor(actorId);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(actorId, actor.ActorId);
    }

    [Fact]
    public void Constructor_WithActorFactory_CreatesActor()
    {
        // Arrange
        var actorId = "stateless-worker-2";
        var factory = new ActorFactory();

        // Act
        var actor = new TestStatelessActor(actorId, factory);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(actorId, actor.ActorId);
    }

    [Fact]
    public async Task OnActivateAsync_CompletesSuccessfully()
    {
        // Arrange
        var actor = new TestStatelessActor("stateless-worker-3");

        // Act
        await actor.OnActivateAsync();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task OnDeactivateAsync_CompletesSuccessfully()
    {
        // Arrange
        var actor = new TestStatelessActor("stateless-worker-4");

        // Act
        await actor.OnDeactivateAsync();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task ProcessMessage_ReturnsExpectedResult()
    {
        // Arrange
        var actor = new TestStatelessActor("stateless-worker-5");
        var message = "test-message";

        // Act
        var result = await actor.ProcessMessageAsync(message);

        // Assert
        Assert.Equal($"Processed: {message}", result);
    }

    [Fact]
    public void ActorAttribute_SupportsStatelessProperty()
    {
        // Arrange & Act
        var attribute = new ActorAttribute { Stateless = true };

        // Assert
        Assert.True(attribute.Stateless);
    }

    [Fact]
    public void StatelessWorkerAttribute_HasDefaultValues()
    {
        // Arrange & Act
        var attribute = new StatelessWorkerAttribute();

        // Assert
        Assert.Equal(1, attribute.MinInstances);
        Assert.Equal(10, attribute.MaxInstances);
    }

    [Fact]
    public void StatelessWorkerAttribute_CanSetCustomValues()
    {
        // Arrange & Act
        var attribute = new StatelessWorkerAttribute
        {
            MinInstances = 5,
            MaxInstances = 50
        };

        // Assert
        Assert.Equal(5, attribute.MinInstances);
        Assert.Equal(50, attribute.MaxInstances);
    }

    [Fact]
    public void MultipleStatelessActors_WithSameId_CanBeCreated()
    {
        // Arrange
        var actorId = "stateless-worker-multi";

        // Act
        var actor1 = new TestStatelessActor(actorId);
        var actor2 = new TestStatelessActor(actorId);

        // Assert - Both actors have same ID but are different instances
        Assert.Equal(actorId, actor1.ActorId);
        Assert.Equal(actorId, actor2.ActorId);
        Assert.NotSame(actor1, actor2);
    }

    [Fact]
    public async Task StatelessActor_WithoutState_ProcessesConcurrently()
    {
        // Arrange
        var actorId = "stateless-worker-concurrent";
        var actor1 = new TestStatelessActor(actorId);
        var actor2 = new TestStatelessActor(actorId);

        // Act
        var task1 = actor1.ProcessMessageAsync("message-1");
        var task2 = actor2.ProcessMessageAsync("message-2");

        await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal("Processed: message-1", task1.Result);
        Assert.Equal("Processed: message-2", task2.Result);
    }
}

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
