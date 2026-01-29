using Quark.EventSourcing;

namespace Quark.Tests;

public class InMemoryEventStoreTests
{
    private class TestEvent : DomainEvent
    {
        public string? Description { get; set; }
    }

    [Fact]
    public async Task AppendEventsAsync_FirstSave_ReturnsVersionOne()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" }
        };

        // Act
        var version = await store.AppendEventsAsync("actor1", events, null);

        // Assert
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task AppendEventsAsync_MultipleEvents_IncrementsSequence()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events1 = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" },
            new TestEvent { Description = "Event 2" }
        };
        var events2 = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 3" }
        };

        // Act
        var version1 = await store.AppendEventsAsync("actor1", events1, null);
        var version2 = await store.AppendEventsAsync("actor1", events2, version1);

        // Assert
        Assert.Equal(2, version1);
        Assert.Equal(3, version2);
    }

    [Fact]
    public async Task AppendEventsAsync_IncorrectVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" }
        };
        await store.AppendEventsAsync("actor1", events, null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EventStoreConcurrencyException>(async () =>
            await store.AppendEventsAsync("actor1", events, 999));

        Assert.Equal(999, ex.ExpectedVersion);
        Assert.Equal(1, ex.ActualVersion);
    }

    [Fact]
    public async Task ReadEventsAsync_ReturnsAllEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" },
            new TestEvent { Description = "Event 2" },
            new TestEvent { Description = "Event 3" }
        };
        await store.AppendEventsAsync("actor1", events, null);

        // Act
        var result = await store.ReadEventsAsync("actor1");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].SequenceNumber);
        Assert.Equal(2, result[1].SequenceNumber);
        Assert.Equal(3, result[2].SequenceNumber);
    }

    [Fact]
    public async Task ReadEventsAsync_WithFromVersion_ReturnsSubset()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" },
            new TestEvent { Description = "Event 2" },
            new TestEvent { Description = "Event 3" }
        };
        await store.AppendEventsAsync("actor1", events, null);

        // Act
        var result = await store.ReadEventsAsync("actor1", fromVersion: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].SequenceNumber);
        Assert.Equal(3, result[1].SequenceNumber);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_NoEvents_ReturnsZero()
    {
        // Arrange
        var store = new InMemoryEventStore();

        // Act
        var version = await store.GetCurrentVersionAsync("actor1");

        // Assert
        Assert.Equal(0, version);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_WithEvents_ReturnsHighestVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events = new List<DomainEvent>
        {
            new TestEvent { Description = "Event 1" },
            new TestEvent { Description = "Event 2" }
        };
        await store.AppendEventsAsync("actor1", events, null);

        // Act
        var version = await store.GetCurrentVersionAsync("actor1");

        // Assert
        Assert.Equal(2, version);
    }

    [Fact]
    public async Task SaveAndLoadSnapshot_StoresAndRetrievesSnapshot()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var snapshot = new { Counter = 42, Name = "Test" };

        // Act
        await store.SaveSnapshotAsync("actor1", snapshot, 10);
        var result = await store.LoadSnapshotAsync("actor1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Value.Version);
    }

    [Fact]
    public async Task LoadSnapshot_NoSnapshot_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryEventStore();

        // Act
        var result = await store.LoadSnapshotAsync("actor1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task MultipleActors_EventsAreIsolated()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var events1 = new List<DomainEvent> { new TestEvent { Description = "Actor1 Event" } };
        var events2 = new List<DomainEvent> { new TestEvent { Description = "Actor2 Event" } };

        // Act
        await store.AppendEventsAsync("actor1", events1, null);
        await store.AppendEventsAsync("actor2", events2, null);

        var result1 = await store.ReadEventsAsync("actor1");
        var result2 = await store.ReadEventsAsync("actor2");

        // Assert
        Assert.Single(result1);
        Assert.Single(result2);
        Assert.Equal("actor1", result1[0].ActorId);
        Assert.Equal("actor2", result2[0].ActorId);
    }
}
