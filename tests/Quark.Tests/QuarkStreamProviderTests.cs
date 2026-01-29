using Quark.Abstractions.Streaming;
using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for the QuarkStreamProvider implementation.
/// </summary>
public class QuarkStreamProviderTests
{
    [Fact]
    public void QuarkStreamProvider_GetStream_WithValidParameters_ReturnsStreamHandle()
    {
        // Arrange
        var provider = new QuarkStreamProvider();

        // Act
        var stream = provider.GetStream<string>("orders/processed", "order-123");

        // Assert
        Assert.NotNull(stream);
        Assert.Equal("orders/processed", stream.StreamId.Namespace);
        Assert.Equal("order-123", stream.StreamId.Key);
    }

    [Fact]
    public void QuarkStreamProvider_GetStream_CalledTwiceWithSameId_ReturnsSameInstance()
    {
        // Arrange
        var provider = new QuarkStreamProvider();

        // Act
        var stream1 = provider.GetStream<string>("orders/processed", "order-123");
        var stream2 = provider.GetStream<string>("orders/processed", "order-123");

        // Assert
        Assert.Same(stream1, stream2);
    }

    [Fact]
    public void QuarkStreamProvider_GetStream_WithStreamId_ReturnsStreamHandle()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var streamId = new StreamId("orders/processed", "order-123");

        // Act
        var stream = provider.GetStream<string>(streamId);

        // Assert
        Assert.NotNull(stream);
        Assert.Equal(streamId, stream.StreamId);
    }

    [Fact]
    public async Task StreamHandle_PublishAsync_WithSubscribers_InvokesCallbacks()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("orders/processed", "order-123");
        var receivedMessages = new List<string>();

        await stream.SubscribeAsync(async msg =>
        {
            receivedMessages.Add(msg);
            await Task.CompletedTask;
        });

        // Act
        await stream.PublishAsync("test-message");

        // Assert
        Assert.Single(receivedMessages);
        Assert.Equal("test-message", receivedMessages[0]);
    }

    [Fact]
    public async Task StreamHandle_PublishAsync_WithMultipleSubscribers_InvokesAllCallbacks()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("orders/processed", "order-123");
        var receivedMessages1 = new List<string>();
        var receivedMessages2 = new List<string>();

        await stream.SubscribeAsync(async msg =>
        {
            receivedMessages1.Add(msg);
            await Task.CompletedTask;
        });

        await stream.SubscribeAsync(async msg =>
        {
            receivedMessages2.Add(msg);
            await Task.CompletedTask;
        });

        // Act
        await stream.PublishAsync("test-message");

        // Assert
        Assert.Single(receivedMessages1);
        Assert.Single(receivedMessages2);
        Assert.Equal("test-message", receivedMessages1[0]);
        Assert.Equal("test-message", receivedMessages2[0]);
    }

    [Fact]
    public async Task StreamHandle_UnsubscribeAsync_RemovesSubscriber()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("orders/processed", "order-123");
        var receivedMessages = new List<string>();

        var subscription = await stream.SubscribeAsync(async msg =>
        {
            receivedMessages.Add(msg);
            await Task.CompletedTask;
        });

        // Act
        await subscription.UnsubscribeAsync();
        await stream.PublishAsync("test-message");

        // Assert
        Assert.Empty(receivedMessages);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task StreamSubscriptionHandle_Dispose_UnsubscribesFromStream()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("orders/processed", "order-123");
        var receivedMessages = new List<string>();

        var subscription = await stream.SubscribeAsync(async msg =>
        {
            receivedMessages.Add(msg);
            await Task.CompletedTask;
        });

        // Act
        subscription.Dispose();
        await stream.PublishAsync("test-message");

        // Assert
        Assert.Empty(receivedMessages);
        Assert.False(subscription.IsActive);
    }
}
