using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

public class SupervisionTests
{
    [Fact]
    public async Task SpawnChildAsync_WithFactory_CreatesChild()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-1");

        // Act
        var child = await parent.SpawnChildAsync<ChildActor>("child-1");

        // Assert
        Assert.NotNull(child);
        Assert.Equal("child-1", child.ActorId);
    }

    [Fact]
    public async Task GetChildren_AfterSpawning_ReturnsChildren()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-2");

        // Act
        await parent.SpawnChildAsync<ChildActor>("child-1");
        await parent.SpawnChildAsync<ChildActor>("child-2");
        var children = parent.GetChildren();

        // Assert
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.ActorId == "child-1");
        Assert.Contains(children, c => c.ActorId == "child-2");
    }

    [Fact]
    public async Task OnChildFailureAsync_DefaultImplementation_ReturnsRestart()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-3");
        var child = await parent.SpawnChildAsync<ChildActor>("child-3");
        var exception = new Exception("Test exception");
        var context = new ChildFailureContext(child, exception);

        // Act
        var directive = await parent.OnChildFailureAsync(context);

        // Assert
        Assert.Equal(SupervisionDirective.Restart, directive);
    }

    [Fact]
    public async Task OnChildFailureAsync_CustomImplementation_ReturnsCustomDirective()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<CustomSupervisorActor>("parent-4");
        var child = await parent.SpawnChildAsync<ChildActor>("child-4");
        var exception = new InvalidOperationException("Invalid operation");
        var context = new ChildFailureContext(child, exception);

        // Act
        var directive = await parent.OnChildFailureAsync(context);

        // Assert
        Assert.Equal(SupervisionDirective.Stop, directive);
    }

    [Fact]
    public void ChildFailureContext_Constructor_SetsProperties()
    {
        // Arrange
        var actor = new ChildActor("test-actor");
        var exception = new Exception("Test exception");

        // Act
        var context = new ChildFailureContext(actor, exception);

        // Assert
        Assert.Same(actor, context.Child);
        Assert.Same(exception, context.Exception);
    }

    [Fact]
    public void ChildFailureContext_NullChild_ThrowsArgumentNullException()
    {
        // Arrange
        var exception = new Exception("Test exception");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChildFailureContext(null!, exception));
    }

    [Fact]
    public void ChildFailureContext_NullException_ThrowsArgumentNullException()
    {
        // Arrange
        var actor = new ChildActor("test-actor");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChildFailureContext(actor, null!));
    }

    [Fact]
    public async Task SpawnChildAsync_WithoutFactory_ThrowsInvalidOperationException()
    {
        // Arrange
        var parent = new ParentActorWithoutFactory("parent-5");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => parent.SpawnChildAsync<ChildActor>("child-5"));
    }

    [Fact]
    public void GetChildren_NoChildren_ReturnsEmptyCollection()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-6");

        // Act
        var children = parent.GetChildren();

        // Assert
        Assert.Empty(children);
    }

    [Fact]
    public void SupervisionDirective_HasExpectedValues()
    {
        // Assert - verify all expected directive values exist
        Assert.True(Enum.IsDefined(typeof(SupervisionDirective), SupervisionDirective.Resume));
        Assert.True(Enum.IsDefined(typeof(SupervisionDirective), SupervisionDirective.Restart));
        Assert.True(Enum.IsDefined(typeof(SupervisionDirective), SupervisionDirective.Stop));
        Assert.True(Enum.IsDefined(typeof(SupervisionDirective), SupervisionDirective.Escalate));
    }

    [Fact]
    public async Task SpawnChildAsync_WithDuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-7");
        await parent.SpawnChildAsync<ChildActor>("duplicate-child");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => parent.SpawnChildAsync<ChildActor>("duplicate-child"));
    }

    [Fact]
    public async Task SpawnChildAsync_WithNullActorId_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-8");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => parent.SpawnChildAsync<ChildActor>(null!));
    }

    [Fact]
    public async Task SpawnChildAsync_WithEmptyActorId_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-9");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => parent.SpawnChildAsync<ChildActor>(""));
    }

    [Fact]
    public async Task SpawnChildAsync_WithWhitespaceActorId_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ActorFactory();
        var parent = factory.CreateActor<ParentActor>("parent-10");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => parent.SpawnChildAsync<ChildActor>("   "));
    }
}

// Test actors for supervision tests
[Actor]
public class ParentActor : ActorBase
{
    public ParentActor(string actorId) : base(actorId)
    {
    }

    public ParentActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}

[Actor]
public class ParentActorWithoutFactory : ActorBase
{
    public ParentActorWithoutFactory(string actorId) : base(actorId)
    {
    }
}

[Actor]
public class ChildActor : ActorBase
{
    public ChildActor(string actorId) : base(actorId)
    {
    }

    public ChildActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}

[Actor]
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
        // Custom supervision: stop on InvalidOperationException, restart on others
        if (context.Exception is InvalidOperationException)
        {
            return Task.FromResult(SupervisionDirective.Stop);
        }

        return Task.FromResult(SupervisionDirective.Restart);
    }
}
