// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.Logging;
using Moq;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Hosting;
using Xunit;

namespace Quark.Tests;

public class IdleDeactivationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotDeactivateActors()
    {
        // Arrange
        var siloMock = new Mock<IQuarkSilo>();
        var activityTrackerMock = new Mock<IActorActivityTracker>();
        var policyMock = new Mock<IActorDeactivationPolicy>();
        var loggerMock = new Mock<ILogger<IdleDeactivationService>>();

        var options = new ServerlessActorOptions
        {
            Enabled = false,
            CheckInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new IdleDeactivationService(
            siloMock.Object,
            activityTrackerMock.Object,
            policyMock.Object,
            options,
            loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(cts.Token);

        // Assert
        siloMock.Verify(s => s.GetActiveActors(), Times.Never);
        policyMock.Verify(
            p => p.ShouldDeactivate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoActiveActors_DoesNotCallPolicy()
    {
        // Arrange
        var siloMock = new Mock<IQuarkSilo>();
        siloMock.Setup(s => s.GetActiveActors()).Returns(Array.Empty<IActor>());

        var activityTrackerMock = new Mock<IActorActivityTracker>();
        var policyMock = new Mock<IActorDeactivationPolicy>();
        var loggerMock = new Mock<ILogger<IdleDeactivationService>>();

        var options = new ServerlessActorOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new IdleDeactivationService(
            siloMock.Object,
            activityTrackerMock.Object,
            policyMock.Object,
            options,
            loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(cts.Token);

        // Assert
        siloMock.Verify(s => s.GetActiveActors(), Times.AtLeastOnce);
        policyMock.Verify(
            p => p.ShouldDeactivate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithIdleActor_CallsOnDeactivate()
    {
        // Arrange
        var actorMock = new Mock<IActor>();
        actorMock.Setup(a => a.ActorId).Returns("test-actor-1");

        var siloMock = new Mock<IQuarkSilo>();
        siloMock.Setup(s => s.GetActiveActors()).Returns(new[] { actorMock.Object });

        var metrics = new ActorActivityMetrics(
            actorId: "test-actor-1",
            actorType: "TestActor",
            queueDepth: 0,
            activeCallCount: 0,
            lastActivityTime: DateTimeOffset.UtcNow.AddMinutes(-10),
            hasActiveStreams: false,
            activityScore: 0.0);

        var activityTrackerMock = new Mock<IActorActivityTracker>();
        activityTrackerMock
            .Setup(at => at.GetActivityMetricsAsync("test-actor-1"))
            .ReturnsAsync(metrics);

        var policyMock = new Mock<IActorDeactivationPolicy>();
        policyMock
            .Setup(p => p.ShouldDeactivate(
                "test-actor-1",
                "TestActor",
                It.IsAny<DateTimeOffset>(),
                0,
                0))
            .Returns(true);

        var loggerMock = new Mock<ILogger<IdleDeactivationService>>();

        var options = new ServerlessActorOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new IdleDeactivationService(
            siloMock.Object,
            activityTrackerMock.Object,
            policyMock.Object,
            options,
            loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(cts.Token);

        // Assert
        actorMock.Verify(a => a.OnDeactivateAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveActor_DoesNotDeactivate()
    {
        // Arrange
        var actorMock = new Mock<IActor>();
        actorMock.Setup(a => a.ActorId).Returns("test-actor-1");

        var siloMock = new Mock<IQuarkSilo>();
        siloMock.Setup(s => s.GetActiveActors()).Returns(new[] { actorMock.Object });

        var metrics = new ActorActivityMetrics(
            actorId: "test-actor-1",
            actorType: "TestActor",
            queueDepth: 0,
            activeCallCount: 0,
            lastActivityTime: DateTimeOffset.UtcNow.AddSeconds(-10),
            hasActiveStreams: false,
            activityScore: 0.0);

        var activityTrackerMock = new Mock<IActorActivityTracker>();
        activityTrackerMock
            .Setup(at => at.GetActivityMetricsAsync("test-actor-1"))
            .ReturnsAsync(metrics);

        var policyMock = new Mock<IActorDeactivationPolicy>();
        policyMock
            .Setup(p => p.ShouldDeactivate(
                "test-actor-1",
                "TestActor",
                It.IsAny<DateTimeOffset>(),
                0,
                0))
            .Returns(false); // Policy says don't deactivate

        var loggerMock = new Mock<ILogger<IdleDeactivationService>>();

        var options = new ServerlessActorOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new IdleDeactivationService(
            siloMock.Object,
            activityTrackerMock.Object,
            policyMock.Object,
            options,
            loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(cts.Token);

        // Assert
        actorMock.Verify(a => a.OnDeactivateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMinimumActiveActors()
    {
        // Arrange
        var actor1Mock = new Mock<IActor>();
        actor1Mock.Setup(a => a.ActorId).Returns("test-actor-1");

        var actor2Mock = new Mock<IActor>();
        actor2Mock.Setup(a => a.ActorId).Returns("test-actor-2");

        var siloMock = new Mock<IQuarkSilo>();
        siloMock.Setup(s => s.GetActiveActors()).Returns(new[] { actor1Mock.Object, actor2Mock.Object });

        var metrics1 = new ActorActivityMetrics(
            actorId: "test-actor-1",
            actorType: "TestActor",
            queueDepth: 0,
            activeCallCount: 0,
            lastActivityTime: DateTimeOffset.UtcNow.AddMinutes(-10),
            hasActiveStreams: false,
            activityScore: 0.0);

        var metrics2 = new ActorActivityMetrics(
            actorId: "test-actor-2",
            actorType: "TestActor",
            queueDepth: 0,
            activeCallCount: 0,
            lastActivityTime: DateTimeOffset.UtcNow.AddMinutes(-10),
            hasActiveStreams: false,
            activityScore: 0.0);

        var activityTrackerMock = new Mock<IActorActivityTracker>();
        activityTrackerMock
            .Setup(at => at.GetActivityMetricsAsync("test-actor-1"))
            .ReturnsAsync(metrics1);
        activityTrackerMock
            .Setup(at => at.GetActivityMetricsAsync("test-actor-2"))
            .ReturnsAsync(metrics2);

        var policyMock = new Mock<IActorDeactivationPolicy>();
        policyMock
            .Setup(p => p.ShouldDeactivate(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                0,
                0))
            .Returns(true); // Policy says deactivate both

        var loggerMock = new Mock<ILogger<IdleDeactivationService>>();

        var options = new ServerlessActorOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromMilliseconds(10),
            MinimumActiveActors = 1 // Keep at least 1 actor active
        };

        var service = new IdleDeactivationService(
            siloMock.Object,
            activityTrackerMock.Object,
            policyMock.Object,
            options,
            loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(cts.Token);

        // Assert - Only one actor should be deactivated, one should remain
        var totalDeactivations = 0;
        if (actor1Mock.Invocations.Any(i => i.Method.Name == nameof(IActor.OnDeactivateAsync)))
            totalDeactivations++;
        if (actor2Mock.Invocations.Any(i => i.Method.Name == nameof(IActor.OnDeactivateAsync)))
            totalDeactivations++;

        Assert.Equal(1, totalDeactivations);
    }
}
