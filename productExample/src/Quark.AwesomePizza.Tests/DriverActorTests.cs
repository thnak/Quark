using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Silo.Actors;

namespace Quark.AwesomePizza.Tests;

/// <summary>
/// Tests for DriverActor functionality
/// </summary>
public class DriverActorTests
{
    [Fact]
    public async Task InitializeAsync_WithName_CreatesDriver()
    {
        // Arrange
        var actor = new DriverActor("driver-1");
        await actor.OnActivateAsync();

        // Act
        var driver = await actor.InitializeAsync("John Doe");

        // Assert
        Assert.NotNull(driver);
        Assert.Equal("driver-1", driver.DriverId);
        Assert.Equal("John Doe", driver.Name);
        Assert.Equal(DriverStatus.Available, driver.Status);
    }

    [Fact]
    public async Task UpdateLocationAsync_WithCoordinates_UpdatesLocation()
    {
        // Arrange
        var actor = new DriverActor("driver-2");
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Jane Smith");

        var timestamp = DateTime.UtcNow;

        // Act
        var driver = await actor.UpdateLocationAsync(40.7128, -74.0060, timestamp);

        // Assert
        Assert.NotNull(driver.CurrentLocation);
        Assert.Equal(40.7128, driver.CurrentLocation.Latitude);
        Assert.Equal(-74.0060, driver.CurrentLocation.Longitude);
        Assert.Equal(timestamp, driver.CurrentLocation.Timestamp);
    }

    [Fact]
    public async Task AssignOrderAsync_WhenAvailable_AssignsOrder()
    {
        // Arrange
        var actor = new DriverActor("driver-3");
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Bob Wilson");

        // Act
        var driver = await actor.AssignOrderAsync("order-123");

        // Assert
        Assert.Equal(DriverStatus.Busy, driver.Status);
        Assert.Equal("order-123", driver.CurrentOrderId);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_WhenBusy_MarksAvailable()
    {
        // Arrange
        var actor = new DriverActor("driver-4");
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Alice Johnson");
        await actor.AssignOrderAsync("order-123");

        // Act
        var driver = await actor.CompleteDeliveryAsync();

        // Assert
        Assert.Equal(DriverStatus.Available, driver.Status);
        Assert.Null(driver.CurrentOrderId);
        Assert.Equal(1, driver.DeliveredToday);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenAvailable_ReturnsTrue()
    {
        // Arrange
        var actor = new DriverActor("driver-5");
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Tom Brown");

        // Act
        var isAvailable = await actor.IsAvailableAsync();

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenBusy_ReturnsFalse()
    {
        // Arrange
        var actor = new DriverActor("driver-6");
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Sarah Davis");
        await actor.AssignOrderAsync("order-456");

        // Act
        var isAvailable = await actor.IsAvailableAsync();

        // Assert
        Assert.False(isAvailable);
    }
}
