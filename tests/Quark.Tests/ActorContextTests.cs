using Quark.Core.Actors;

namespace Quark.Tests;

public class ActorContextTests
{
    [Fact]
    public void ActorContext_Constructor_SetsProperties()
    {
        // Arrange & Act
        var context = new ActorContext("actor-1", "correlation-1", "request-1");

        // Assert
        Assert.Equal("actor-1", context.ActorId);
        Assert.Equal("correlation-1", context.CorrelationId);
        Assert.Equal("request-1", context.RequestId);
    }

    [Fact]
    public void ActorContext_Constructor_GeneratesIdsWhenNotProvided()
    {
        // Arrange & Act
        var context = new ActorContext("actor-2");

        // Assert
        Assert.Equal("actor-2", context.ActorId);
        Assert.NotNull(context.CorrelationId);
        Assert.NotNull(context.RequestId);
    }

    [Fact]
    public void ActorContext_SetMetadata_StoresValue()
    {
        // Arrange
        var context = new ActorContext("actor-3");

        // Act
        context.SetMetadata("key1", "value1");
        context.SetMetadata("key2", 42);

        // Assert
        Assert.Equal("value1", context.GetMetadata<string>("key1"));
        Assert.Equal(42, context.GetMetadata<int>("key2"));
    }

    [Fact]
    public void ActorContext_GetMetadata_ReturnsDefaultWhenNotFound()
    {
        // Arrange
        var context = new ActorContext("actor-4");

        // Act
        var result = context.GetMetadata<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ActorContext_Metadata_ReturnsReadOnlyDictionary()
    {
        // Arrange
        var context = new ActorContext("actor-5");
        context.SetMetadata("key1", "value1");

        // Act
        var metadata = context.Metadata;

        // Assert
        Assert.Contains("key1", metadata.Keys);
        Assert.Equal("value1", metadata["key1"]);
    }

    [Fact]
    public void ActorContext_Current_PropagatesAcrossScope()
    {
        // Arrange
        var context = new ActorContext("actor-6");

        // Act
        using (ActorContext.CreateScope(context))
        {
            var current = ActorContext.Current;

            // Assert
            Assert.NotNull(current);
            Assert.Equal("actor-6", current!.ActorId);
        }

        // After scope disposal, current should be null
        Assert.Null(ActorContext.Current);
    }

    [Fact]
    public void ActorContext_CreateScope_RestoresPreviousContext()
    {
        // Arrange
        var context1 = new ActorContext("actor-7");
        var context2 = new ActorContext("actor-8");

        // Act
        using (ActorContext.CreateScope(context1))
        {
            Assert.Equal("actor-7", ActorContext.Current!.ActorId);

            using (ActorContext.CreateScope(context2))
            {
                Assert.Equal("actor-8", ActorContext.Current!.ActorId);
            }

            // After inner scope, should be back to context1
            Assert.Equal("actor-7", ActorContext.Current!.ActorId);
        }

        // After all scopes, should be null
        Assert.Null(ActorContext.Current);
    }

    [Fact]
    public async Task ActorContext_Current_PropagatesAcrossAsync()
    {
        // Arrange
        var context = new ActorContext("actor-9");

        // Act & Assert
        using (ActorContext.CreateScope(context))
        {
            Assert.Equal("actor-9", ActorContext.Current!.ActorId);

            await Task.Delay(10);

            // Context should still be available after async
            Assert.Equal("actor-9", ActorContext.Current!.ActorId);
        }
    }

    [Fact]
    public async Task ActorBase_OnActivateAsync_CreatesContext()
    {
        // Arrange
        var actor = new TestActorForContext("actor-10");

        // Act
        await actor.OnActivateAsync();

        // Assert
        // Context is created during activation but disposed after the method completes
        // So we verify it was accessible during activation by checking the captured value
        Assert.Equal("actor-10", actor.CapturedContextActorId);
    }

    [Fact]
    public async Task ActorBase_OnDeactivateAsync_CreatesContext()
    {
        // Arrange
        var actor = new TestActorForContext("actor-11");

        // Act
        await actor.OnDeactivateAsync();

        // Assert
        // Context is created during deactivation but disposed after the method completes
        Assert.Equal("actor-11", actor.CapturedDeactivationContextActorId);
    }

    [Fact]
    public async Task ActorBase_Context_IsAccessibleDuringExecution()
    {
        // Arrange
        var actor = new TestActorForContext("actor-12");

        // Act
        var result = await actor.GetContextActorIdAsync();

        // Assert
        Assert.Equal("actor-12", result);
    }
}

// Test actor for ActorContext integration tests