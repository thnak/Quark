using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

namespace Quark.Demo.PizzaDash.Shared.Actors;

/// <summary>
/// Actor representing a delivery driver with GPS tracking capabilities.
/// </summary>
[Actor(Name = "DeliveryDriver", Reentrant = false)]
public class DeliveryDriverActor : ActorBase
{
    private GpsLocation? _currentLocation;
    private string? _assignedOrderId;
    private bool _isAvailable = true;

    public DeliveryDriverActor(string actorId) : base(actorId)
    {
    }

    /// <summary>
    /// Updates the driver's current GPS location.
    /// </summary>
    public Task<GpsLocation> UpdateLocationAsync(double latitude, double longitude)
    {
        _currentLocation = new GpsLocation(latitude, longitude, DateTime.UtcNow);
        return Task.FromResult(_currentLocation);
    }

    /// <summary>
    /// Gets the driver's current location.
    /// </summary>
    public Task<GpsLocation?> GetLocationAsync()
    {
        return Task.FromResult(_currentLocation);
    }

    /// <summary>
    /// Assigns an order to this driver.
    /// </summary>
    public Task AssignOrderAsync(string orderId)
    {
        if (!_isAvailable)
            throw new InvalidOperationException($"Driver {ActorId} is not available");

        _assignedOrderId = orderId;
        _isAvailable = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks the current delivery as complete and makes driver available again.
    /// </summary>
    public Task CompleteDeliveryAsync()
    {
        _assignedOrderId = null;
        _isAvailable = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the currently assigned order ID.
    /// </summary>
    public Task<string?> GetAssignedOrderAsync()
    {
        return Task.FromResult(_assignedOrderId);
    }

    /// <summary>
    /// Checks if the driver is available for assignment.
    /// </summary>
    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(_isAvailable);
    }
}
