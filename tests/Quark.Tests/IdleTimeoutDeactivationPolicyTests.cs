// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

public class IdleTimeoutDeactivationPolicyTests
{
    [Fact]
    public void Constructor_WithZeroTimeout_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new IdleTimeoutDeactivationPolicy(TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_WithNegativeTimeout_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new IdleTimeoutDeactivationPolicy(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ShouldDeactivate_WithPendingMessages_ReturnsFalse()
    {
        // Arrange
        var policy = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(5));
        var lastActivityTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 5,
            activeCallCount: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldDeactivate_WithActiveCalls_ReturnsFalse()
    {
        // Arrange
        var policy = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(5));
        var lastActivityTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldDeactivate_WithIdleActorBeyondTimeout_ReturnsTrue()
    {
        // Arrange
        var policy = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(5));
        var lastActivityTime = DateTimeOffset.UtcNow.AddMinutes(-10); // Idle for 10 minutes

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldDeactivate_WithIdleActorJustBelowTimeout_ReturnsFalse()
    {
        // Arrange
        var policy = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(5));
        var lastActivityTime = DateTimeOffset.UtcNow.AddMinutes(-4).AddSeconds(-59); // Just under 5 minutes

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldDeactivate_WithIdleActorAtExactTimeout_ReturnsTrue()
    {
        // Arrange
        var timeout = TimeSpan.FromMinutes(5);
        var policy = new IdleTimeoutDeactivationPolicy(timeout);
        var lastActivityTime = DateTimeOffset.UtcNow.Add(-timeout);

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldDeactivate_WithRecentActivity_ReturnsFalse()
    {
        // Arrange
        var policy = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(5));
        var lastActivityTime = DateTimeOffset.UtcNow.AddSeconds(-30); // 30 seconds ago

        // Act
        var result = policy.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldDeactivate_WithDifferentTimeouts_WorksCorrectly()
    {
        // Arrange
        var shortTimeout = new IdleTimeoutDeactivationPolicy(TimeSpan.FromSeconds(30));
        var longTimeout = new IdleTimeoutDeactivationPolicy(TimeSpan.FromMinutes(10));
        var lastActivityTime = DateTimeOffset.UtcNow.AddMinutes(-1); // 1 minute idle

        // Act
        var shortResult = shortTimeout.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        var longResult = longTimeout.ShouldDeactivate(
            actorId: "test-actor",
            actorType: "TestActor",
            lastActivityTime: lastActivityTime,
            currentQueueDepth: 0,
            activeCallCount: 0);

        // Assert
        Assert.True(shortResult, "Short timeout should trigger deactivation");
        Assert.False(longResult, "Long timeout should not trigger deactivation");
    }
}
