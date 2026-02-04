using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

public class MailboxTests
{
    [Fact]
    public async Task ChannelMailbox_PostAsync_SuccessfullyQueuesMessage()
    {
        // Arrange
        var actor = new MailboxTestActor("test-1");
        var mailbox = new ChannelMailbox(actor);

        // Act
        var message = new ActorMethodMessage<string>("TestMethod");
        var result = await mailbox.PostAsync(message);

        // Assert
        Assert.True(result);
        Assert.Equal(1, mailbox.MessageCount);
    }

    [Fact]
    public async Task ChannelMailbox_StartStop_ManagesProcessingState()
    {
        // Arrange
        var actor = new MailboxTestActor("test-2");
        var mailbox = new ChannelMailbox(actor);

        // Act
        await mailbox.StartAsync();
        var isProcessingAfterStart = mailbox.IsProcessing;

        await mailbox.StopAsync();
        var isProcessingAfterStop = mailbox.IsProcessing;

        // Assert
        Assert.True(isProcessingAfterStart);
        Assert.False(isProcessingAfterStop);
    }

    [Fact]
    public void ChannelMailbox_ActorId_ReturnsCorrectId()
    {
        // Arrange
        var actor = new MailboxTestActor("test-3");
        var mailbox = new ChannelMailbox(actor);

        // Assert
        Assert.Equal("test-3", mailbox.ActorId);
    }

    [Fact]
    public async Task ChannelMailbox_Dispose_StopsProcessing()
    {
        // Arrange
        var actor = new MailboxTestActor("test-4");
        var mailbox = new ChannelMailbox(actor);
        await mailbox.StartAsync();

        // Act
        mailbox.Dispose();

        // Assert - no exception should be thrown
        Assert.False(mailbox.IsProcessing);
    }

    [Fact]
    public async Task ChannelMailbox_MessageCount_UpdatesCorrectly()
    {
        // Arrange
        var actor = new MailboxTestActor("test-5");
        var mailbox = new ChannelMailbox(actor, capacity: 10);

        // Act
        await mailbox.PostAsync(new ActorMethodMessage<string>("Method1"));
        await mailbox.PostAsync(new ActorMethodMessage<string>("Method2"));
        await mailbox.PostAsync(new ActorMethodMessage<string>("Method3"));

        // Assert
        Assert.Equal(3, mailbox.MessageCount);
    }
}

// Test actor for mailbox tests