using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

public class MailboxWithDeadLetterQueueTests
{
    [Fact]
    public async Task ChannelMailbox_WithDLQ_CapturesFailedMessages()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var actor = new FailingActor("failing-actor-1");
        var mailbox = new ChannelMailbox(actor, capacity: 10, deadLetterQueue: dlq);

        // Start the mailbox
        await mailbox.StartAsync();

        // Act - post a message (will fail with NotImplementedException from InvokeMethodAsync)
        var message = new ActorMethodMessage<object>("ThrowError", "test error");
        await mailbox.PostAsync(message);

        // Wait for processing
        await Task.Delay(1000);

        // Stop the mailbox
        await mailbox.StopAsync();

        // Assert - message should be in DLQ due to NotImplementedException
        Assert.Equal(1, dlq.MessageCount);
        var deadLetters = await dlq.GetAllAsync();
        Assert.Single(deadLetters);
        Assert.Equal(actor.ActorId, deadLetters[0].ActorId);
        Assert.IsType<NotImplementedException>(deadLetters[0].Exception);
    }

    [Fact]
    public async Task ChannelMailbox_WithoutDLQ_DoesNotCaptureFailures()
    {
        // Arrange
        var actor = new FailingActor("failing-actor-2");
        var mailbox = new ChannelMailbox(actor, capacity: 10, deadLetterQueue: null);

        // Start the mailbox
        await mailbox.StartAsync();

        // Act - post a message (will fail with NotImplementedException, no DLQ configured)
        var message = new ActorMethodMessage<object>("ThrowError", "test error");
        await mailbox.PostAsync(message);

        // Wait for processing
        await Task.Delay(1000);

        // Stop the mailbox
        await mailbox.StopAsync();

        // Assert - no DLQ, but mailbox should handle error gracefully and continue
        Assert.False(mailbox.IsProcessing);
    }

    [Fact]
    public async Task ChannelMailbox_WithDLQ_ContinuesAfterFailure()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var actor = new FailingActor("failing-actor-3");
        var mailbox = new ChannelMailbox(actor, capacity: 10, deadLetterQueue: dlq);

        // Start the mailbox
        await mailbox.StartAsync();

        // Act - post multiple messages (all will fail with NotImplementedException)
        await mailbox.PostAsync(new ActorMethodMessage<object>("ThrowError", "error 1"));
        await mailbox.PostAsync(new ActorMethodMessage<object>("ThrowError", "error 2"));

        // Wait for processing
        await Task.Delay(1000);

        // Stop the mailbox
        await mailbox.StopAsync();

        // Assert - both failures captured in DLQ
        Assert.Equal(2, dlq.MessageCount);
        Assert.Equal(0, mailbox.MessageCount); // All messages processed
    }

    [Fact]
    public async Task ChannelMailbox_WithDLQ_CapturesExceptionDetails()
    {
        // Arrange
        var dlq = new InMemoryDeadLetterQueue();
        var actor = new FailingActor("failing-actor-4");
        var mailbox = new ChannelMailbox(actor, capacity: 10, deadLetterQueue: dlq);

        // Start the mailbox
        await mailbox.StartAsync();

        // Act
        var message = new ActorMethodMessage<object>("ThrowError", "specific error message");
        await mailbox.PostAsync(message);

        // Wait for processing
        await Task.Delay(1000);

        // Stop the mailbox
        await mailbox.StopAsync();

        // Assert
        var deadLetters = await dlq.GetAllAsync();
        Assert.Single(deadLetters);
        var deadLetter = deadLetters.First();
        Assert.IsType<NotImplementedException>(deadLetter.Exception);
        Assert.Equal(message.MessageId, deadLetter.Message.MessageId);
    }
}

// Test actor that throws exceptions
[Actor]
public class FailingActor : ActorBase
{
    public FailingActor(string actorId) : base(actorId)
    {
    }

    public Task<string> ThrowError(string errorMessage)
    {
        throw new InvalidOperationException(errorMessage);
    }

    public Task<string> SuccessMethod(string input)
    {
        return Task.FromResult($"Success: {input}");
    }
}
