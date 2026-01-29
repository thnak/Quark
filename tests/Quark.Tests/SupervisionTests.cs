using Quark.Core;

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
        Assert.Equal(0, (int)SupervisionDirective.Resume);
        Assert.Equal(1, (int)SupervisionDirective.Restart);
        Assert.Equal(2, (int)SupervisionDirective.Stop);
        Assert.Equal(3, (int)SupervisionDirective.Escalate);
    }
}

// Test actors for supervision tests
public class ParentActor : ActorBase
{
    public ParentActor(string actorId) : base(actorId)
    {
    }

    public ParentActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}

public class ParentActorWithoutFactory : ActorBase
{
    public ParentActorWithoutFactory(string actorId) : base(actorId)
    {
    }
}

public class ChildActor : ActorBase
{
    public ChildActor(string actorId) : base(actorId)
    {
    }

    public ChildActor(string actorId, IActorFactory actorFactory) : base(actorId, actorFactory)
    {
    }
}

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
