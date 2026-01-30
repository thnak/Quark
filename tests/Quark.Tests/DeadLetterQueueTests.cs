using Quark.Core.Actors;

namespace Quark.Tests;

public class DeadLetterQueueTests
{
    [Fact]
    public async Task InMemoryDeadLetterQueue_EnqueueAsync_AddsMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        var exception = new InvalidOperationException("Test error");

        // Act
        await dlq.EnqueueAsync(message, "test-actor-1", exception);

        // Assert
        Assert.Equal(1, dlq.MessageCount);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_GetAllAsync_ReturnsAllMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message1 = new ActorMethodMessage<string>("Method1");
        var message2 = new ActorMethodMessage<string>("Method2");
        var exception = new InvalidOperationException("Test error");

        await dlq.EnqueueAsync(message1, "actor-1", exception);
        await dlq.EnqueueAsync(message2, "actor-2", exception);

        // Act
        var messages = await dlq.GetAllAsync();

        // Assert
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_GetByActorAsync_FiltersCorrectly()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message1 = new ActorMethodMessage<string>("Method1");
        var message2 = new ActorMethodMessage<string>("Method2");
        var message3 = new ActorMethodMessage<string>("Method3");
        var exception = new InvalidOperationException("Test error");

        await dlq.EnqueueAsync(message1, "actor-1", exception);
        await dlq.EnqueueAsync(message2, "actor-2", exception);
        await dlq.EnqueueAsync(message3, "actor-1", exception);

        // Act
        var messagesForActor1 = await dlq.GetByActorAsync("actor-1");
        var messagesForActor2 = await dlq.GetByActorAsync("actor-2");

        // Assert
        Assert.Equal(2, messagesForActor1.Count);
        Assert.Single(messagesForActor2);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_RemoveAsync_RemovesMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        var exception = new InvalidOperationException("Test error");

        await dlq.EnqueueAsync(message, "test-actor", exception);

        // Act
        var removed = await dlq.RemoveAsync(message.MessageId);

        // Assert
        Assert.True(removed);
        Assert.Equal(0, dlq.MessageCount);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_RemoveAsync_ReturnsFalseForNonExistentMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();

        // Act
        var removed = await dlq.RemoveAsync("non-existent-id");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ClearAsync_RemovesAllMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var exception = new InvalidOperationException("Test error");

        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method1"), "actor-1", exception);
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method2"), "actor-2", exception);
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method3"), "actor-3", exception);

        // Act
        await dlq.ClearAsync();

        // Assert
        Assert.Equal(0, dlq.MessageCount);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_RespectsMaxMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue(maxMessages: 3);
        var exception = new InvalidOperationException("Test error");

        // Act - add 4 messages (exceeds max)
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method1"), "actor-1", exception);
        await Task.Delay(10); // Small delay to ensure different timestamps
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method2"), "actor-2", exception);
        await Task.Delay(10);
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method3"), "actor-3", exception);
        await Task.Delay(10);
        await dlq.EnqueueAsync(new ActorMethodMessage<string>("Method4"), "actor-4", exception);

        // Assert - should only have 3 messages (oldest removed)
        Assert.Equal(3, dlq.MessageCount);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_StoresExceptionDetails()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        var exception = new InvalidOperationException("Specific test error");

        // Act
        await dlq.EnqueueAsync(message, "test-actor", exception);
        var messages = await dlq.GetAllAsync();

        // Assert
        var deadLetter = messages.First();
        Assert.Equal("test-actor", deadLetter.ActorId);
        Assert.Equal(exception.Message, deadLetter.Exception.Message);
        Assert.IsType<InvalidOperationException>(deadLetter.Exception);
    }

    [Fact]
    public void InMemoryDeadLetterQueue_ThrowsOnInvalidMaxMessages()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new InMemoryDeadLetterQueue(0));
        Assert.Throws<ArgumentException>(() => new InMemoryDeadLetterQueue(-1));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_EnqueueAsync_ThrowsOnNullMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var exception = new InvalidOperationException("Test error");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dlq.EnqueueAsync(null!, "actor-1", exception));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_EnqueueAsync_ThrowsOnNullActorId()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        var exception = new InvalidOperationException("Test error");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            dlq.EnqueueAsync(message, null!, exception));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_EnqueueAsync_ThrowsOnNullException()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dlq.EnqueueAsync(message, "actor-1", null!));
    }
}
