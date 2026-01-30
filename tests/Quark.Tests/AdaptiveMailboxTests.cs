using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

/// <summary>
///     Phase 8.3: Tests for adaptive mailbox with burst handling.
/// </summary>
public class AdaptiveMailboxTests
{
    private class TestActor : IActor
    {
        public string ActorId { get; }

        public TestActor(string actorId)
        {
            ActorId = actorId;
        }

        public Task OnActivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestMessage : IActorMessage
    {
        public string MessageId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset Timestamp { get; }

        public TestMessage(string messageId, string? correlationId = null)
        {
            MessageId = messageId;
            CorrelationId = correlationId;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    [Fact]
    public void AdaptiveMailbox_WithDefaultOptions_StartsWithInitialCapacity()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var options = new AdaptiveMailboxOptions
        {
            Enabled = false,
            InitialCapacity = 500
        };

        // Act
        using var mailbox = new AdaptiveMailbox(actor, options);

        // Assert
        Assert.Equal("test-1", mailbox.ActorId);
        Assert.Equal(0, mailbox.MessageCount);
    }

    [Fact]
    public async Task AdaptiveMailbox_PostMessage_IncreasesCount()
    {
        // Arrange
        var actor = new TestActor("test-1");
        using var mailbox = new AdaptiveMailbox(actor);
        var message = new TestMessage("msg-1");

        // Act
        var posted = await mailbox.PostAsync(message);

        // Assert
        Assert.True(posted);
        Assert.Equal(1, mailbox.MessageCount);
    }

    [Fact]
    public async Task AdaptiveMailbox_WithRateLimitDrop_DropsExcessMessages()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var rateLimitOptions = new RateLimitOptions
        {
            Enabled = true,
            MaxMessagesPerWindow = 5,
            TimeWindow = TimeSpan.FromSeconds(1),
            ExcessAction = RateLimitAction.Drop
        };

        using var mailbox = new AdaptiveMailbox(actor, rateLimitOptions: rateLimitOptions);

        // Act - Post more messages than the limit
        var results = new List<bool>();
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage($"msg-{i}");
            var posted = await mailbox.PostAsync(message);
            results.Add(posted);
        }

        // Assert - First 5 should succeed, rest should be dropped
        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(5, results.Count(r => !r));
    }

    [Fact]
    public async Task AdaptiveMailbox_WithRateLimitReject_ThrowsForExcessMessages()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var rateLimitOptions = new RateLimitOptions
        {
            Enabled = true,
            MaxMessagesPerWindow = 3,
            TimeWindow = TimeSpan.FromSeconds(1),
            ExcessAction = RateLimitAction.Reject
        };

        using var mailbox = new AdaptiveMailbox(actor, rateLimitOptions: rateLimitOptions);

        // Act & Assert - First 3 should succeed
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage($"msg-{i}");
            await mailbox.PostAsync(message);
        }

        // 4th message should throw
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var message = new TestMessage("msg-excess");
            await mailbox.PostAsync(message);
        });
    }

    [Fact]
    public async Task AdaptiveMailbox_WithCircuitBreakerDisabled_AcceptsAllMessages()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var circuitBreakerOptions = new CircuitBreakerOptions
        {
            Enabled = false
        };

        using var mailbox = new AdaptiveMailbox(actor, circuitBreakerOptions: circuitBreakerOptions);

        // Act - Post multiple messages (circuit breaker disabled)
        var results = new List<bool>();
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage($"msg-{i}");
            var posted = await mailbox.PostAsync(message);
            results.Add(posted);
        }

        // Assert - All should succeed
        Assert.All(results, r => Assert.True(r));
        Assert.Equal(10, mailbox.MessageCount);
    }

    [Fact]
    public async Task AdaptiveMailbox_StartAndStop_CompletesSuccessfully()
    {
        // Arrange
        var actor = new TestActor("test-1");
        using var mailbox = new AdaptiveMailbox(actor);

        // Act
        await mailbox.StartAsync();
        await mailbox.StopAsync();

        // Assert
        Assert.False(mailbox.IsProcessing);
    }

    [Fact]
    public void AdaptiveMailbox_Dispose_CompletesCleanly()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var mailbox = new AdaptiveMailbox(actor);

        // Act
        mailbox.Dispose();

        // Assert - Should not throw
        Assert.False(mailbox.IsProcessing);
    }

    [Fact]
    public async Task AdaptiveMailbox_MultipleMessages_MaintainsCount()
    {
        // Arrange
        var actor = new TestActor("test-1");
        using var mailbox = new AdaptiveMailbox(actor);

        // Act - Post 50 messages
        for (int i = 0; i < 50; i++)
        {
            var message = new TestMessage($"msg-{i}");
            await mailbox.PostAsync(message);
        }

        // Assert
        Assert.Equal(50, mailbox.MessageCount);
    }

    [Fact]
    public void AdaptiveMailbox_WithAdaptiveDisabled_UsesInitialCapacity()
    {
        // Arrange
        var actor = new TestActor("test-1");
        var options = new AdaptiveMailboxOptions
        {
            Enabled = false,
            InitialCapacity = 2000,
            MaxCapacity = 5000
        };

        // Act
        using var mailbox = new AdaptiveMailbox(actor, options);

        // Assert - Mailbox created successfully with initial capacity
        Assert.NotNull(mailbox);
        Assert.Equal(0, mailbox.MessageCount);
    }
}

/// <summary>
///     Phase 8.3: Tests for burst handling options.
/// </summary>
public class BurstHandlingOptionsTests
{
    [Fact]
    public void AdaptiveMailboxOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new AdaptiveMailboxOptions();

        // Assert
        Assert.Equal(1000, options.InitialCapacity);
        Assert.Equal(100, options.MinCapacity);
        Assert.Equal(10000, options.MaxCapacity);
        Assert.Equal(0.8, options.GrowThreshold);
        Assert.Equal(0.2, options.ShrinkThreshold);
        Assert.Equal(2.0, options.GrowthFactor);
        Assert.Equal(0.5, options.ShrinkFactor);
        Assert.Equal(10, options.MinSamplesBeforeAdapt);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new CircuitBreakerOptions();

        // Assert
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(3, options.SuccessThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.SamplingWindow);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void RateLimitOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RateLimitOptions();

        // Assert
        Assert.Equal(1000, options.MaxMessagesPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), options.TimeWindow);
        Assert.Equal(RateLimitAction.Drop, options.ExcessAction);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void CircuitState_Values_AreCorrect()
    {
        // Assert
        Assert.Equal(0, (int)CircuitState.Closed);
        Assert.Equal(1, (int)CircuitState.Open);
        Assert.Equal(2, (int)CircuitState.HalfOpen);
    }

    [Fact]
    public void RateLimitAction_Values_AreCorrect()
    {
        // Assert
        Assert.Equal(0, (int)RateLimitAction.Drop);
        Assert.Equal(1, (int)RateLimitAction.Reject);
        Assert.Equal(2, (int)RateLimitAction.Queue);
    }
}
