using Quark.Abstractions.Migration;
using Quark.Core.Actors.Migration;
using Xunit;

namespace Quark.Tests;

public class ActorActivityTrackerTests
{
    [Fact]
    public void RecordMessageEnqueued_IncreasesQueueDepth()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordMessageEnqueued(actorId, actorType);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.QueueDepth);
        Assert.Equal(actorId, metrics.ActorId);
        Assert.Equal(actorType, metrics.ActorType);
    }

    [Fact]
    public void RecordMessageDequeued_DecreasesQueueDepth()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordMessageDequeued(actorId, actorType);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.QueueDepth);
    }

    [Fact]
    public void RecordCallStarted_IncreasesActiveCallCount()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordCallStarted(actorId, actorType);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ActiveCallCount);
    }

    [Fact]
    public void RecordCallCompleted_DecreasesActiveCallCount()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordCallStarted(actorId, actorType);
        tracker.RecordCallStarted(actorId, actorType);
        tracker.RecordCallCompleted(actorId, actorType);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ActiveCallCount);
    }

    [Fact]
    public void RecordStreamActivity_TracksStreamSubscriptions()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordStreamActivity(actorId, actorType, subscribed: true);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.HasActiveStreams);
    }

    [Fact]
    public void ActivityScore_ReflectsActorActivity()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act - Create a hot actor
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordCallStarted(actorId, actorType);
        tracker.RecordStreamActivity(actorId, actorType, subscribed: true);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.ActivityScore > 0.5, "Hot actor should have high activity score");
        Assert.True(metrics.IsHot, "Actor should be considered hot");
    }

    [Fact]
    public void IsHot_TrueWhenActiveCallsExist()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act
        tracker.RecordCallStarted(actorId, actorType);
        var metrics = tracker.GetActivityMetricsAsync(actorId).Result;

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.IsHot, "Actor with active calls should be hot");
    }

    [Fact]
    public async Task IsCold_TrueWhenNoActivity()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        const string actorType = "TestActor";

        // Act - Record minimal activity and let it age
        tracker.RecordMessageEnqueued(actorId, actorType);
        tracker.RecordMessageDequeued(actorId, actorType);
        await Task.Delay(100); // Allow time to pass
        var metrics = await tracker.GetActivityMetricsAsync(actorId);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.QueueDepth);
        Assert.Equal(0, metrics.ActiveCallCount);
    }

    [Fact]
    public async Task GetMigrationPriorityListAsync_ReturnsColdActorsFirst()
    {
        // Arrange
        var tracker = new ActorActivityTracker();

        // Create a hot actor
        tracker.RecordCallStarted("hot-actor", "TestActor");
        tracker.RecordMessageEnqueued("hot-actor", "TestActor");
        tracker.RecordMessageEnqueued("hot-actor", "TestActor");

        // Create a cold actor
        tracker.RecordMessageEnqueued("cold-actor", "TestActor");
        tracker.RecordMessageDequeued("cold-actor", "TestActor");

        // Act
        var priorityList = await tracker.GetMigrationPriorityListAsync();

        // Assert
        Assert.Equal(2, priorityList.Count);
        var firstActor = priorityList.First();
        Assert.Equal("cold-actor", firstActor.ActorId);
    }

    [Fact]
    public async Task GetAllActivityMetricsAsync_ReturnsAllTrackedActors()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        tracker.RecordMessageEnqueued("actor-1", "TestActor");
        tracker.RecordMessageEnqueued("actor-2", "TestActor");
        tracker.RecordMessageEnqueued("actor-3", "TestActor");

        // Act
        var allMetrics = await tracker.GetAllActivityMetricsAsync();

        // Assert
        Assert.Equal(3, allMetrics.Count);
    }

    [Fact]
    public async Task RemoveActor_RemovesActorFromTracking()
    {
        // Arrange
        var tracker = new ActorActivityTracker();
        const string actorId = "test-actor-1";
        tracker.RecordMessageEnqueued(actorId, "TestActor");

        // Act
        tracker.RemoveActor(actorId);
        var metrics = await tracker.GetActivityMetricsAsync(actorId);

        // Assert
        Assert.Null(metrics);
    }

    [Fact]
    public async Task GetActivityMetricsAsync_ReturnsNullForNonExistentActor()
    {
        // Arrange
        var tracker = new ActorActivityTracker();

        // Act
        var metrics = await tracker.GetActivityMetricsAsync("non-existent-actor");

        // Assert
        Assert.Null(metrics);
    }
}
