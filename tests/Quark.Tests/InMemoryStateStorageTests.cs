using Quark.Abstractions.Persistence;
using Quark.Core.Persistence;

namespace Quark.Tests;

public class InMemoryStateStorageTests
{
    private class TestState
    {
        public string? Value { get; set; }
        public int Counter { get; set; }
    }

    [Fact]
    public async Task LoadAsync_NonExistentState_ReturnsNull()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();

        // Act
        var result = await storage.LoadAsync("actor1", "state1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoadAsync_StoresAndRetrievesState()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state = new TestState { Value = "test", Counter = 42 };

        // Act
        await storage.SaveAsync("actor1", "state1", state);
        var result = await storage.LoadAsync("actor1", "state1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Value);
        Assert.Equal(42, result.Counter);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingState()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state1 = new TestState { Value = "test1", Counter = 1 };
        var state2 = new TestState { Value = "test2", Counter = 2 };

        // Act
        await storage.SaveAsync("actor1", "state1", state1);
        await storage.SaveAsync("actor1", "state1", state2);
        var result = await storage.LoadAsync("actor1", "state1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test2", result.Value);
        Assert.Equal(2, result.Counter);
    }

    [Fact]
    public async Task DeleteAsync_RemovesState()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state = new TestState { Value = "test", Counter = 42 };

        // Act
        await storage.SaveAsync("actor1", "state1", state);
        await storage.DeleteAsync("actor1", "state1");
        var result = await storage.LoadAsync("actor1", "state1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadWithVersionAsync_NonExistentState_ReturnsNull()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();

        // Act
        var result = await storage.LoadWithVersionAsync("actor1", "state1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveWithVersionAsync_FirstSave_CreatesStateWithVersionOne()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state = new TestState { Value = "test", Counter = 42 };

        // Act
        var version = await storage.SaveWithVersionAsync("actor1", "state1", state, null);
        var result = await storage.LoadWithVersionAsync("actor1", "state1");

        // Assert
        Assert.Equal(1, version);
        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal("test", result.State.Value);
        Assert.Equal(42, result.State.Counter);
    }

    [Fact]
    public async Task SaveWithVersionAsync_CorrectVersion_UpdatesState()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state1 = new TestState { Value = "test1", Counter = 1 };
        var state2 = new TestState { Value = "test2", Counter = 2 };

        // Act
        var version1 = await storage.SaveWithVersionAsync("actor1", "state1", state1, null);
        var version2 = await storage.SaveWithVersionAsync("actor1", "state1", state2, version1);
        var result = await storage.LoadWithVersionAsync("actor1", "state1");

        // Assert
        Assert.Equal(1, version1);
        Assert.Equal(2, version2);
        Assert.NotNull(result);
        Assert.Equal(2, result.Version);
        Assert.Equal("test2", result.State.Value);
    }

    [Fact]
    public async Task SaveWithVersionAsync_IncorrectVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state1 = new TestState { Value = "test1", Counter = 1 };
        var state2 = new TestState { Value = "test2", Counter = 2 };

        // Act
        await storage.SaveWithVersionAsync("actor1", "state1", state1, null);

        // Assert
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await storage.SaveWithVersionAsync("actor1", "state1", state2, 999));

        Assert.Equal(999, ex.ExpectedVersion);
        Assert.Equal(1, ex.ActualVersion);
    }

    [Fact]
    public async Task SaveWithVersionAsync_ExpectedVersionOnNonExistentState_ThrowsConcurrencyException()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state = new TestState { Value = "test", Counter = 42 };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await storage.SaveWithVersionAsync("actor1", "state1", state, 1));

        Assert.Equal(1, ex.ExpectedVersion);
        Assert.Equal(0, ex.ActualVersion);
    }

    [Fact]
    public async Task MultipleActors_StatesAreIsolated()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state1 = new TestState { Value = "actor1", Counter = 1 };
        var state2 = new TestState { Value = "actor2", Counter = 2 };

        // Act
        await storage.SaveAsync("actor1", "state1", state1);
        await storage.SaveAsync("actor2", "state1", state2);

        var result1 = await storage.LoadAsync("actor1", "state1");
        var result2 = await storage.LoadAsync("actor2", "state1");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("actor1", result1.Value);
        Assert.Equal("actor2", result2.Value);
    }

    [Fact]
    public async Task MultipleStateNames_StatesAreIsolated()
    {
        // Arrange
        var storage = new InMemoryStateStorage<TestState>();
        var state1 = new TestState { Value = "state1", Counter = 1 };
        var state2 = new TestState { Value = "state2", Counter = 2 };

        // Act
        await storage.SaveAsync("actor1", "state1", state1);
        await storage.SaveAsync("actor1", "state2", state2);

        var result1 = await storage.LoadAsync("actor1", "state1");
        var result2 = await storage.LoadAsync("actor1", "state2");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("state1", result1.Value);
        Assert.Equal("state2", result2.Value);
    }
}
