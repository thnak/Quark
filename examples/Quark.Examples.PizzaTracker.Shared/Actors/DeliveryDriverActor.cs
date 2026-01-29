using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Shared.Actors;

/// <summary>
/// Actor that represents a delivery driver and tracks their GPS location.
/// </summary>
[Actor(Name = "DeliveryDriver", Reentrant = false)]
public class DeliveryDriverActor : ActorBase
{
    private string? _currentOrderId;
    private GpsLocation? _currentLocation;
    private readonly string _driverName;

    public DeliveryDriverActor(string actorId) : base(actorId)
    {
        _driverName = $"Driver-{actorId}";
    }

    /// <summary>
    /// Assigns an order to this driver.
    /// </summary>
    public Task AssignOrderAsync(string orderId)
    {
        _currentOrderId = orderId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the driver's current GPS location.
    /// </summary>
    public Task UpdateLocationAsync(double latitude, double longitude)
    {
        _currentLocation = new GpsLocation(latitude, longitude, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the driver's current location.
    /// </summary>
    public Task<GpsLocation?> GetLocationAsync()
    {
        return Task.FromResult(_currentLocation);
    }

    /// <summary>
    /// Gets the current order ID assigned to this driver.
    /// </summary>
    public Task<string?> GetCurrentOrderIdAsync()
    {
        return Task.FromResult(_currentOrderId);
    }

    /// <summary>
    /// Marks the delivery as complete.
    /// </summary>
    public Task CompleteDeliveryAsync()
    {
        _currentOrderId = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the driver's name.
    /// </summary>
    public string GetDriverName() => _driverName;
}
