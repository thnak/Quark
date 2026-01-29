using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;
using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for the StreamBroker and implicit stream subscriptions.
/// </summary>
public class StreamBrokerTests
{
    [Fact]
    public void StreamBroker_RegisterImplicitSubscription_WithValidParameters_Succeeds()
    {
        // Arrange
        var broker = new StreamBroker();

        // Act & Assert (no exception thrown)
        broker.RegisterImplicitSubscription("orders/processed", typeof(TestStreamActor), typeof(string));
    }

    [Fact]
    public void StreamBroker_RegisterImplicitSubscription_WithNullNamespace_ThrowsArgumentException()
    {
        // Arrange
        var broker = new StreamBroker();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            broker.RegisterImplicitSubscription(null!, typeof(TestStreamActor), typeof(string)));
    }

    [Fact]
    public void StreamBroker_RegisterImplicitSubscription_WithNullActorType_ThrowsArgumentNullException()
    {
        // Arrange
        var broker = new StreamBroker();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            broker.RegisterImplicitSubscription("orders/processed", null!, typeof(string)));
    }

    [Fact]
    public void StreamBroker_RegisterImplicitSubscription_WithNullMessageType_ThrowsArgumentNullException()
    {
        // Arrange
        var broker = new StreamBroker();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            broker.RegisterImplicitSubscription("orders/processed", typeof(TestStreamActor), null!));
    }

    [Fact]
    public async Task StreamBroker_NotifyImplicitSubscribers_WithMatchingSubscription_DoesNotThrow()
    {
        // Arrange
        var factory = new ActorFactory();
        var broker = new StreamBroker(factory);
        var provider = new QuarkStreamProvider(broker);
        
        broker.RegisterImplicitSubscription("orders/processed", typeof(TestStreamActor), typeof(string));

        var streamId = new StreamId("orders/processed", "test-actor-1");
        var stream = provider.GetStream<string>(streamId);

        // Act - publish a message to the stream
        await stream.PublishAsync("test-message");

        // Wait a bit for async processing
        await Task.Delay(100);

        // Assert - verify the actor was activated and received the message
        var actor = factory.GetOrCreateActor<TestStreamActor>("test-actor-1");
        Assert.NotEmpty(actor.ReceivedMessages);
        Assert.Equal("test-message", actor.ReceivedMessages[0]);
    }

    [Fact]
    public void StreamRegistry_SetBroker_WithValidBroker_Succeeds()
    {
        // Arrange
        var broker = new StreamBroker();

        // Act
        StreamRegistry.SetBroker(broker);

        // Assert
        Assert.Same(broker, StreamRegistry.GetBroker());
    }

    [Fact]
    public void StreamRegistry_SetBroker_WithNullBroker_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => StreamRegistry.SetBroker(null!));
    }

    [Fact]
    public void StreamRegistry_RegisterImplicitSubscription_WithBroker_Succeeds()
    {
        // Arrange
        var broker = new StreamBroker();
        
        // Save the current broker state
        var originalBroker = StreamRegistry.GetBroker();
        
        try
        {
            // Act
            StreamRegistry.SetBroker(broker);
            
            // Assert - should not throw
            StreamRegistry.RegisterImplicitSubscription("test", typeof(TestStreamActor), typeof(string));
        }
        finally
        {
            // Restore original broker if any
            if (originalBroker != null)
            {
                StreamRegistry.SetBroker(originalBroker);
            }
        }
    }
}

/// <summary>
/// Test actor for stream testing.
/// </summary>
[Actor(Name = "TestStream")]
[QuarkStream("orders/processed")]
public class TestStreamActor : ActorBase, IStreamConsumer<string>
{
    public List<string> ReceivedMessages { get; } = new();

    public TestStreamActor(string actorId) : base(actorId)
    {
    }

    public Task OnStreamMessageAsync(string message, StreamId streamId, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add(message);
        return Task.CompletedTask;
    }
}
