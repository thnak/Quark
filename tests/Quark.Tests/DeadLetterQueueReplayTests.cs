using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for Dead Letter Queue replay functionality.
/// </summary>
public class DeadLetterQueueReplayTests
{
    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayAsync_SuccessfullyReplaysMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        var exception = new InvalidOperationException("Test error");
        
        await dlq.EnqueueAsync(message, "test-actor-1", exception);

        var replayedMessages = new List<IActorMessage>();
        IMailbox? TestMailboxProvider(string actorId)
        {
            var mockMailbox = new MockMailbox(actorId, replayedMessages);
            return mockMailbox;
        }

        // Act
        var replayed = await dlq.ReplayAsync(message.MessageId, TestMailboxProvider);

        // Assert
        Assert.True(replayed);
        Assert.Equal(0, dlq.MessageCount); // Message removed after successful replay
        Assert.Single(replayedMessages);
        Assert.Equal(message.MessageId, replayedMessages[0].MessageId);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayAsync_ReturnsFalseForNonExistentMessage()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        IMailbox? TestMailboxProvider(string actorId) => null;

        // Act
        var replayed = await dlq.ReplayAsync("non-existent-id", TestMailboxProvider);

        // Assert
        Assert.False(replayed);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayAsync_ReturnsFalseWhenMailboxNotFound()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        await dlq.EnqueueAsync(message, "test-actor-1", new InvalidOperationException());

        IMailbox? TestMailboxProvider(string actorId) => null; // No mailbox available

        // Act
        var replayed = await dlq.ReplayAsync(message.MessageId, TestMailboxProvider);

        // Assert
        Assert.False(replayed);
        Assert.Equal(1, dlq.MessageCount); // Message remains in DLQ
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayBatchAsync_ReplaysMultipleMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message1 = new ActorMethodMessage<string>("Method1");
        var message2 = new ActorMethodMessage<string>("Method2");
        var message3 = new ActorMethodMessage<string>("Method3");
        
        await dlq.EnqueueAsync(message1, "actor-1", new InvalidOperationException());
        await dlq.EnqueueAsync(message2, "actor-1", new InvalidOperationException());
        await dlq.EnqueueAsync(message3, "actor-2", new InvalidOperationException());

        var replayedMessages = new List<IActorMessage>();
        IMailbox? TestMailboxProvider(string actorId)
        {
            return new MockMailbox(actorId, replayedMessages);
        }

        var messageIds = new[] { message1.MessageId, message2.MessageId, message3.MessageId };

        // Act
        var replayed = await dlq.ReplayBatchAsync(messageIds, TestMailboxProvider);

        // Assert
        Assert.Equal(3, replayed.Count);
        Assert.Equal(0, dlq.MessageCount); // All messages removed
        Assert.Equal(3, replayedMessages.Count);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayBatchAsync_SkipsFailedMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message1 = new ActorMethodMessage<string>("Method1");
        var message2 = new ActorMethodMessage<string>("Method2");
        
        await dlq.EnqueueAsync(message1, "actor-1", new InvalidOperationException());
        await dlq.EnqueueAsync(message2, "actor-2", new InvalidOperationException());

        var replayedMessages = new List<IActorMessage>();
        IMailbox? TestMailboxProvider(string actorId)
        {
            // Only provide mailbox for actor-1
            if (actorId == "actor-1")
                return new MockMailbox(actorId, replayedMessages);
            return null;
        }

        var messageIds = new[] { message1.MessageId, message2.MessageId };

        // Act
        var replayed = await dlq.ReplayBatchAsync(messageIds, TestMailboxProvider);

        // Assert
        Assert.Single(replayed); // Only message1 replayed
        Assert.Equal(message1.MessageId, replayed[0]);
        Assert.Equal(1, dlq.MessageCount); // message2 still in DLQ
        Assert.Single(replayedMessages);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayByActorAsync_ReplaysAllMessagesForActor()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message1 = new ActorMethodMessage<string>("Method1");
        var message2 = new ActorMethodMessage<string>("Method2");
        var message3 = new ActorMethodMessage<string>("Method3");
        
        await dlq.EnqueueAsync(message1, "actor-1", new InvalidOperationException());
        await dlq.EnqueueAsync(message2, "actor-1", new InvalidOperationException());
        await dlq.EnqueueAsync(message3, "actor-2", new InvalidOperationException());

        var replayedMessages = new List<IActorMessage>();
        IMailbox? TestMailboxProvider(string actorId)
        {
            return new MockMailbox(actorId, replayedMessages);
        }

        // Act
        var replayed = await dlq.ReplayByActorAsync("actor-1", TestMailboxProvider);

        // Assert
        Assert.Equal(2, replayed.Count);
        Assert.Contains(message1.MessageId, replayed);
        Assert.Contains(message2.MessageId, replayed);
        Assert.Equal(1, dlq.MessageCount); // Only message3 (actor-2) remains
        Assert.Equal(2, replayedMessages.Count);
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayAsync_ThrowsOnNullMailboxProvider()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var message = new ActorMethodMessage<string>("TestMethod");
        await dlq.EnqueueAsync(message, "test-actor", new InvalidOperationException());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            dlq.ReplayAsync(message.MessageId, null!));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayBatchAsync_ThrowsOnNullMailboxProvider()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            dlq.ReplayBatchAsync(new[] { "test-id" }, null!));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ReplayByActorAsync_ThrowsOnNullMailboxProvider()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            dlq.ReplayByActorAsync("test-actor", null!));
    }

    /// <summary>
    /// Mock mailbox for testing replay functionality.
    /// </summary>
    private class MockMailbox : IMailbox
    {
        private readonly List<IActorMessage> _replayedMessages;

        public MockMailbox(string actorId, List<IActorMessage> replayedMessages)
        {
            ActorId = actorId;
            _replayedMessages = replayedMessages;
        }

        public string ActorId { get; }
        public int MessageCount => 0;
        public bool IsProcessing => false;

        public ValueTask<bool> PostAsync(IActorMessage message, CancellationToken cancellationToken = default)
        {
            _replayedMessages.Add(message);
            return ValueTask.FromResult(true);
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
