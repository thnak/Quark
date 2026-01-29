using Quark.Core.Actors;

namespace Quark.Tests;

public class ActorFactoryTests
{
    [Fact]
    public void CreateActor_WithValidId_ReturnsActor()
    {
        // Arrange
        var factory = new ActorFactory();
        var actorId = "test-actor-1";

        // Act
        var actor = factory.CreateActor<TestActor>(actorId);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(actorId, actor.ActorId);
    }

    [Fact]
    public void CreateActor_WithNullId_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ActorFactory();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => factory.CreateActor<TestActor>(null!));
    }

    [Fact]
    public void GetOrCreateActor_FirstCall_CreatesNewActor()
    {
        // Arrange
        var factory = new ActorFactory();
        var actorId = "test-actor-2";

        // Act
        var actor = factory.GetOrCreateActor<TestActor>(actorId);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(actorId, actor.ActorId);
    }

    [Fact]
    public void GetOrCreateActor_SecondCall_ReturnsSameActor()
    {
        // Arrange
        var factory = new ActorFactory();
        var actorId = "test-actor-3";

        // Act
        var actor1 = factory.GetOrCreateActor<TestActor>(actorId);
        var actor2 = factory.GetOrCreateActor<TestActor>(actorId);

        // Assert
        Assert.Same(actor1, actor2);
    }

    [Fact]
    public async Task ActorBase_OnActivateAsync_CanBeCalled()
    {
        // Arrange
        var actor = new TestActor("test-actor-4");

        // Act & Assert - no exception thrown
        await actor.OnActivateAsync();
    }

    [Fact]
    public async Task ActorBase_OnDeactivateAsync_CanBeCalled()
    {
        // Arrange
        var actor = new TestActor("test-actor-5");

        // Act & Assert - no exception thrown
        await actor.OnDeactivateAsync();
    }
}

// Test actor for unit tests
[Quark.Abstractions.Actor]
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
