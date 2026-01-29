using Quark.Abstractions.Persistence;
using Quark.Core.Persistence;

namespace Quark.Tests;

public class StateStorageProviderTests
{
    private class TestState
    {
        public string? Value { get; set; }
    }

    [Fact]
    public void GetStorage_WithoutRegistration_ReturnsInMemoryStorage()
    {
        // Arrange
        var provider = new StateStorageProvider();

        // Act
        var storage = provider.GetStorage<TestState>("test-provider");

        // Assert
        Assert.NotNull(storage);
        Assert.IsType<InMemoryStateStorage<TestState>>(storage);
    }

    [Fact]
    public void GetStorage_MultipleCalls_ReturnsSameInstance()
    {
        // Arrange
        var provider = new StateStorageProvider();

        // Act
        var storage1 = provider.GetStorage<TestState>("test-provider");
        var storage2 = provider.GetStorage<TestState>("test-provider");

        // Assert
        Assert.Same(storage1, storage2);
    }

    [Fact]
    public void GetStorage_DifferentProviders_ReturnsDifferentInstances()
    {
        // Arrange
        var provider = new StateStorageProvider();

        // Act
        var storage1 = provider.GetStorage<TestState>("provider1");
        var storage2 = provider.GetStorage<TestState>("provider2");

        // Assert
        Assert.NotSame(storage1, storage2);
    }

    [Fact]
    public void RegisterStorage_CustomFactory_UsesCustomStorage()
    {
        // Arrange
        var provider = new StateStorageProvider();
        var customStorage = new InMemoryStateStorage<TestState>();

        provider.RegisterStorage("custom", _ => customStorage);

        // Act
        var storage = provider.GetStorage<TestState>("custom");

        // Assert
        Assert.Same(customStorage, storage);
    }

    [Fact]
    public async Task RegisteredStorage_WorksCorrectly()
    {
        // Arrange
        var provider = new StateStorageProvider();
        var customStorage = new InMemoryStateStorage<TestState>();
        provider.RegisterStorage("custom", _ => customStorage);

        var state = new TestState { Value = "test" };

        // Act
        var storage = provider.GetStorage<TestState>("custom");
        await storage.SaveWithVersionAsync("actor1", "state1", state, null);
        var result = await storage.LoadWithVersionAsync("actor1", "state1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.State.Value);
    }
}
