using Quark.Core.Actors;
using Quark.Core.Actors.Pooling;
using Xunit;

namespace Quark.Tests;

/// <summary>
///     Tests for the zero-allocation messaging pooling infrastructure.
/// </summary>
public class MessagePoolingTests
{
    [Fact]
    public void MessageIdGenerator_GeneratesUniqueIds()
    {
        // Arrange & Act
        var id1 = MessageIdGenerator.Generate();
        var id2 = MessageIdGenerator.Generate();
        var id3 = MessageIdGenerator.Generate();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void MessageIdGenerator_GeneratesIncrementingIds()
    {
        // Arrange & Act
        var id1 = long.Parse(MessageIdGenerator.Generate());
        var id2 = long.Parse(MessageIdGenerator.Generate());
        var id3 = long.Parse(MessageIdGenerator.Generate());

        // Assert
        Assert.True(id2 > id1);
        Assert.True(id3 > id2);
    }

    [Fact]
    public void MessageIdGenerator_WithPrefix_GeneratesCorrectFormat()
    {
        // Arrange & Act
        var id = MessageIdGenerator.GenerateWithPrefix("msg-");

        // Assert
        Assert.StartsWith("msg-", id);
        Assert.True(id.Length > 4); // Has numeric part after prefix
    }

    [Fact]
    public void TaskCompletionSourcePool_Rent_ReturnsValidTCS()
    {
        // Arrange
        var pool = new TaskCompletionSourcePool<int>();

        // Act
        var tcs = pool.Rent();

        // Assert
        Assert.NotNull(tcs);
        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact]
    public void TaskCompletionSourcePool_RentReturn_ReusesInstance()
    {
        // Arrange
        var pool = new TaskCompletionSourcePool<int>();
        var tcs = pool.Rent();
        tcs.SetResult(42);

        // Act
        pool.Return(tcs);
        var poolCount = pool.Count;

        // Assert
        Assert.Equal(1, poolCount);
    }

    [Fact]
    public void TaskCompletionSourcePool_Return_DoesNotExceedMaxSize()
    {
        // Arrange
        var pool = new TaskCompletionSourcePool<int>(maxPoolSize: 2);

        // Act - Complete and return 3 TCS instances
        for (int i = 0; i < 3; i++)
        {
            var tcs = pool.Rent();
            tcs.SetResult(i);
            pool.Return(tcs);
        }

        // Assert - Pool should not exceed max size
        Assert.True(pool.Count <= 2);
    }

    [Fact]
    public void ActorMethodMessagePool_Rent_ReturnsValidMessage()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<string>();

        // Act
        var message = pool.Rent("TestMethod", new object?[] { "arg1", 42 });

        // Assert
        Assert.NotNull(message);
        Assert.Equal("TestMethod", message.MethodName);
        Assert.Equal(2, message.Arguments.Length);
        Assert.NotNull(message.CompletionSource);
        Assert.NotNull(message.MessageId);
    }

    [Fact]
    public void ActorMethodMessagePool_Dispose_ReturnsToPool()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<int>();
        var message = pool.Rent("TestMethod", Array.Empty<object?>());
        message.CompletionSource.SetResult(100);

        // Act
        message.Dispose();
        var poolCount = pool.Count;

        // Assert
        Assert.Equal(1, poolCount);
    }

    [Fact]
    public void ActorMethodMessagePool_RentAfterReturn_ReusesMessage()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<int>();
        var message1 = pool.Rent("Method1", Array.Empty<object?>());
        message1.CompletionSource.SetResult(1);
        message1.Dispose();

        // Act
        var message2 = pool.Rent("Method2", Array.Empty<object?>());

        // Assert
        Assert.NotNull(message2);
        Assert.Equal("Method2", message2.MethodName);
        Assert.Equal(0, pool.Count); // Message was taken from pool
    }

    [Fact]
    public void ActorMessageFactory_CreatePooled_ReturnsPooledMessage()
    {
        // Arrange & Act
        var message = ActorMessageFactory.CreatePooled<string>("TestMethod", "arg1", "arg2");

        // Assert
        Assert.NotNull(message);
        Assert.Equal("TestMethod", message.MethodName);
        Assert.Equal(2, message.Arguments.Length);
        Assert.IsType<PooledActorMethodMessage<string>>(message);
    }

    [Fact]
    public void ActorMessageFactory_Create_ReturnsStandardMessage()
    {
        // Arrange & Act
        var message = ActorMessageFactory.Create<int>("TestMethod", 1, 2, 3);

        // Assert
        Assert.NotNull(message);
        Assert.Equal("TestMethod", message.MethodName);
        Assert.Equal(3, message.Arguments.Length);
        Assert.IsType<ActorMethodMessage<int>>(message);
    }

    [Fact]
    public async Task PooledMessage_CompletionSource_WorksCorrectly()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<int>();
        var message = pool.Rent("TestMethod", Array.Empty<object?>());

        // Act
        message.CompletionSource.SetResult(42);
        var result = await message.CompletionSource.Task;

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void PooledMessage_CorrelationId_CanBeSet()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<int>();
        var message = pool.Rent("TestMethod", Array.Empty<object?>());

        // Act
        message.CorrelationId = "test-correlation-123";

        // Assert
        Assert.Equal("test-correlation-123", message.CorrelationId);
    }

    [Fact]
    public void PooledMessage_Timestamp_IsSet()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var pool = new ActorMethodMessagePool<int>();

        // Act
        var message = pool.Rent("TestMethod", Array.Empty<object?>());
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(message.Timestamp >= before);
        Assert.True(message.Timestamp <= after);
    }

    [Fact]
    public void PooledMessage_Reset_UpdatesAllFields()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<string>();
        var message = pool.Rent("Method1", new object?[] { "arg1" });
        var originalId = message.MessageId;
        message.CorrelationId = "corr-1";
        message.CompletionSource.SetResult("result1");

        // Act - Return and rent again (triggers reset)
        message.Dispose();
        var message2 = pool.Rent("Method2", new object?[] { "arg2", "arg3" });

        // Assert
        Assert.Equal("Method2", message2.MethodName);
        Assert.Equal(2, message2.Arguments.Length);
        Assert.Null(message2.CorrelationId);
        Assert.NotEqual(originalId, message2.MessageId);
    }

    [Fact]
    public void PooledMessage_MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var pool = new ActorMethodMessagePool<int>();
        var message = pool.Rent("TestMethod", Array.Empty<object?>());
        message.CompletionSource.SetResult(1);

        // Act & Assert - Should not throw
        message.Dispose();
        message.Dispose();
        message.Dispose();
    }
}
