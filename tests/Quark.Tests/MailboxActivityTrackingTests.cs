using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for mailbox integration with activity tracking (Phase 10.1.1 - Task 1).
/// </summary>
public class MailboxActivityTrackingTests
{
    private class TestActor : IActor
    {
        public string ActorId { get; }
        
        public TestActor(string actorId)
        {
            ActorId = actorId;
        }
        
        public Task OnActivateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnDeactivateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    
    private class TestMessage : IActorMessage
    {
        public string MessageId { get; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; } = null;
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task ChannelMailbox_WithActivityTracker_RecordsMessageEnqueued()
    {
        // Arrange
        var actor = new TestActor("test-actor-1");
        var mockTracker = new Mock<IActorActivityTracker>();
        var mailbox = new ChannelMailbox(
            actor, 
            capacity: 10, 
            deadLetterQueue: null,
            activityTracker: mockTracker.Object,
            actorType: "TestActor");
        
        // Act
        var message = new TestMessage();
        await mailbox.PostAsync(message);
        
        // Assert
        mockTracker.Verify(
            t => t.RecordMessageEnqueued("test-actor-1", "TestActor"),
            Times.Once);
    }

    [Fact]
    public async Task ChannelMailbox_WithoutActivityTracker_DoesNotThrow()
    {
        // Arrange
        var actor = new TestActor("test-actor-2");
        var mailbox = new ChannelMailbox(actor, capacity: 10);
        
        // Act - should not throw even without activity tracker
        var message = new TestMessage();
        var result = await mailbox.PostAsync(message);
        
        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ChannelMailbox_Dispose_RemovesActorFromTracker()
    {
        // Arrange
        var actor = new TestActor("test-actor-3");
        var mockTracker = new Mock<IActorActivityTracker>();
        var mailbox = new ChannelMailbox(
            actor, 
            capacity: 10, 
            deadLetterQueue: null,
            activityTracker: mockTracker.Object,
            actorType: "TestActor");
        
        // Act
        mailbox.Dispose();
        
        // Assert
        mockTracker.Verify(
            t => t.RemoveActor("test-actor-3"),
            Times.Once);
    }
    
    [Fact]
    public void ChannelMailbox_WithoutTracker_DisposeDoesNotThrow()
    {
        // Arrange
        var actor = new TestActor("test-actor-4");
        var mailbox = new ChannelMailbox(actor, capacity: 10);
        
        // Act & Assert - should not throw
        mailbox.Dispose();
    }

    [Fact]
    public async Task ChannelMailbox_MessageCount_ReflectsQueuedMessages()
    {
        // Arrange
        var actor = new TestActor("test-actor-5");
        var mockTracker = new Mock<IActorActivityTracker>();
        var mailbox = new ChannelMailbox(
            actor, 
            capacity: 10, 
            deadLetterQueue: null,
            activityTracker: mockTracker.Object,
            actorType: "TestActor");
        
        // Act
        await mailbox.PostAsync(new TestMessage());
        await mailbox.PostAsync(new TestMessage());
        await mailbox.PostAsync(new TestMessage());
        
        // Assert
        Assert.Equal(3, mailbox.MessageCount);
        mockTracker.Verify(
            t => t.RecordMessageEnqueued("test-actor-5", "TestActor"),
            Times.Exactly(3));
    }
}
