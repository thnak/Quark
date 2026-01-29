using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

public class CallChainContextTests
{
    [Fact]
    public void CallChainContext_Create_GeneratesUniqueChainId()
    {
        // Act
        var context1 = CallChainContext.Create();
        var context2 = CallChainContext.Create();

        // Assert
        Assert.NotEqual(context1.ChainId, context2.ChainId);
    }

    [Fact]
    public void CallChainContext_EnterActor_AddsToChain()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act
        using (context.EnterActor("actor-1", "TestActor"))
        {
            // Assert
            Assert.True(context.IsInCallChain("actor-1", "TestActor"));
        }
    }

    [Fact]
    public void CallChainContext_EnterActor_RemovesOnDispose()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act
        using (context.EnterActor("actor-1", "TestActor"))
        {
            Assert.True(context.IsInCallChain("actor-1", "TestActor"));
        }

        // Assert
        Assert.False(context.IsInCallChain("actor-1", "TestActor"));
    }

    [Fact]
    public void CallChainContext_EnterActor_ThrowsOnCircularDependency()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act & Assert
        using (context.EnterActor("actor-1", "TestActor"))
        {
            var exception = Assert.Throws<ReentrancyException>(() =>
            {
                context.EnterActor("actor-1", "TestActor");
            });

            Assert.Contains("Circular dependency detected", exception.Message);
            Assert.Contains("actor-1", exception.Message);
        }
    }

    [Fact]
    public void CallChainContext_GetCallChainString_ReturnsChain()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act
        using (context.EnterActor("actor-1", "TestActor"))
        using (context.EnterActor("actor-2", "TestActor"))
        using (context.EnterActor("actor-3", "TestActor"))
        {
            var chain = context.GetCallChainString();

            // Assert
            Assert.Contains("TestActor:actor-1", chain);
            Assert.Contains("TestActor:actor-2", chain);
            Assert.Contains("TestActor:actor-3", chain);
        }
    }

    [Fact]
    public void CallChainContext_CreateScope_SetsCurrentContext()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act
        using (context.CreateScope())
        {
            // Assert
            Assert.NotNull(CallChainContext.Current);
            Assert.Equal(context.ChainId, CallChainContext.Current!.ChainId);
        }

        // After disposal
        Assert.Null(CallChainContext.Current);
    }

    [Fact]
    public void CallChainContext_CreateScope_RestoresPrevious()
    {
        // Arrange
        var context1 = CallChainContext.Create();
        var context2 = CallChainContext.Create();

        // Act
        using (context1.CreateScope())
        {
            Assert.Equal(context1.ChainId, CallChainContext.Current!.ChainId);

            using (context2.CreateScope())
            {
                Assert.Equal(context2.ChainId, CallChainContext.Current!.ChainId);
            }

            // After inner scope
            Assert.Equal(context1.ChainId, CallChainContext.Current!.ChainId);
        }
    }

    [Fact]
    public void CallChainContext_CreateChild_HasSameChainId()
    {
        // Arrange
        var parent = CallChainContext.Create();

        // Act
        var child = parent.CreateChild();

        // Assert
        Assert.Equal(parent.ChainId, child.ChainId);
    }

    [Fact]
    public void CallChainContext_CreateChild_HasSeparateCallChain()
    {
        // Arrange
        var parent = CallChainContext.Create();
        
        using (parent.EnterActor("actor-1", "TestActor"))
        {
            // Act
            var child = parent.CreateChild();

            // Assert - Child starts with parent's chain
            Assert.True(child.IsInCallChain("actor-1", "TestActor"));

            // But modifications are independent
            using (child.EnterActor("actor-2", "TestActor"))
            {
                Assert.True(child.IsInCallChain("actor-2", "TestActor"));
                Assert.False(parent.IsInCallChain("actor-2", "TestActor"));
            }
        }
    }

    [Fact]
    public async Task CallChainContext_PropagatesAcrossAsync()
    {
        // Arrange
        var context = CallChainContext.Create();

        // Act & Assert
        using (context.CreateScope())
        {
            Assert.Equal(context.ChainId, CallChainContext.Current!.ChainId);

            await Task.Delay(10);

            // Should still be the same context after async
            Assert.Equal(context.ChainId, CallChainContext.Current!.ChainId);
        }
    }
}
