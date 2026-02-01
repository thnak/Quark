using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Silo.Actors;

namespace Quark.AwesomePizza.Tests;

/// <summary>
/// Tests for MQTT Bridge integration with DriverActor.
/// These tests verify that the message handling logic works correctly.
/// </summary>
public class MqttBridgeTests
{
    [Fact]
    public async Task DriverActor_ReceivesLocationUpdate_ViaSimulatedMqtt()
    {
        // Arrange
        var driverId = "driver-mqtt-test-1";
        var actor = new DriverActor(driverId);
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Test Driver");

        var latitude = 40.7128;
        var longitude = -74.0060;
        var timestamp = DateTime.UtcNow;

        // Act - Simulate what MQTT bridge does
        await actor.UpdateLocationAsync(latitude, longitude, timestamp);

        // Assert
        var state = await actor.GetStateAsync();
        Assert.NotNull(state);
        Assert.NotNull(state.CurrentLocation);
        Assert.Equal(latitude, state.CurrentLocation.Latitude);
        Assert.Equal(longitude, state.CurrentLocation.Longitude);
        Assert.Equal(timestamp, state.CurrentLocation.Timestamp);
    }

    [Fact]
    public async Task DriverActor_ReceivesStatusUpdate_ViaSimulatedMqtt()
    {
        // Arrange
        var driverId = "driver-mqtt-test-2";
        var actor = new DriverActor(driverId);
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Test Driver");

        // Act - Simulate what MQTT bridge does
        await actor.UpdateStatusAsync(DriverStatus.Busy);

        // Assert
        var state = await actor.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(DriverStatus.Busy, state.Status);
    }

    [Fact]
    public async Task DriverActor_MultipleLocationUpdates_TrackMovement()
    {
        // Arrange
        var driverId = "driver-mqtt-test-3";
        var actor = new DriverActor(driverId);
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Test Driver");

        // Act - Simulate GPS tracking over time
        var locations = new[]
        {
            (40.7128, -74.0060),
            (40.7138, -74.0050),
            (40.7148, -74.0040),
            (40.7158, -74.0030)
        };

        foreach (var (lat, lon) in locations)
        {
            await actor.UpdateLocationAsync(lat, lon, DateTime.UtcNow);
            await Task.Delay(100); // Simulate time passing
        }

        // Assert - Final location should be the last one
        var state = await actor.GetStateAsync();
        Assert.NotNull(state);
        Assert.NotNull(state.CurrentLocation);
        Assert.Equal(40.7158, state.CurrentLocation.Latitude);
        Assert.Equal(-74.0030, state.CurrentLocation.Longitude);
    }

    [Fact]
    public async Task DriverActor_OrderAssignment_ThenLocationTracking()
    {
        // Arrange
        var driverId = "driver-mqtt-test-4";
        var actor = new DriverActor(driverId);
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Test Driver");

        // Act - Assign order then track location
        await actor.AssignOrderAsync("order-123");
        await actor.UpdateLocationAsync(40.7128, -74.0060, DateTime.UtcNow);

        // Assert
        var state = await actor.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(DriverStatus.Busy, state.Status);
        Assert.Equal("order-123", state.CurrentOrderId);
        Assert.NotNull(state.CurrentLocation);
    }

    [Theory]
    [InlineData(40.7128, -74.0060)]
    [InlineData(34.0522, -118.2437)]
    [InlineData(51.5074, -0.1278)]
    public async Task DriverActor_VariousLocations_AllProcessedCorrectly(double lat, double lon)
    {
        // Arrange
        var driverId = $"driver-mqtt-test-{lat}";
        var actor = new DriverActor(driverId);
        await actor.OnActivateAsync();
        await actor.InitializeAsync("Test Driver");

        // Act
        await actor.UpdateLocationAsync(lat, lon, DateTime.UtcNow);

        // Assert
        var state = await actor.GetStateAsync();
        Assert.NotNull(state);
        Assert.NotNull(state.CurrentLocation);
        Assert.Equal(lat, state.CurrentLocation.Latitude);
        Assert.Equal(lon, state.CurrentLocation.Longitude);
    }
}
