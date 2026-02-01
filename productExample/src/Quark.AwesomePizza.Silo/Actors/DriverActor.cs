using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that manages a delivery driver's state and location.
/// Receives GPS updates via MQTT bridge and manages delivery assignments.
/// </summary>
[Actor(InterfaceType = typeof(IDriverActor), Reentrant = false)]
public class DriverActor : ActorBase, IDriverActor
{
    private DriverState? _state;

    public DriverActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In full implementation, load state from Redis
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    public Task<DriverState> InitializeAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_state != null)
            throw new InvalidOperationException($"Driver {ActorId} already initialized");

        _state = new DriverState(
            DriverId: ActorId,
            Name: name,
            Status: DriverStatus.Available,
            LastUpdated: DateTime.UtcNow);

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Updates driver location from MQTT telemetry.
    /// Called by MQTT bridge when GPS device publishes location.
    /// </summary>
    public Task<DriverState> UpdateLocationAsync(
        double latitude,
        double longitude,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Driver {ActorId} not initialized");

        var location = new GpsLocation(latitude, longitude, timestamp);
        
        _state = _state with
        {
            CurrentLocation = location,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        
        // If driver has an assigned order, update the order's driver location
        if (_state.CurrentOrderId != null)
        {
            // TODO: Get OrderActor and update location
            // var orderActor = GetActor<OrderActor>(_state.CurrentOrderId);
            // await orderActor.UpdateDriverLocationAsync(location, cancellationToken);
        }

        return Task.FromResult(_state);
    }

    /// <summary>
    /// Assigns an order to this driver.
    /// </summary>
    public Task<DriverState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_state == null)
            throw new InvalidOperationException($"Driver {ActorId} not initialized");

        if (_state.Status != DriverStatus.Available)
            throw new InvalidOperationException($"Driver is not available (current status: {_state.Status})");

        _state = _state with
        {
            Status = DriverStatus.Busy,
            CurrentOrderId = orderId,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Completes the current delivery.
    /// </summary>
    public Task<DriverState> CompleteDeliveryAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Driver {ActorId} not initialized");

        if (_state.Status != DriverStatus.Busy || _state.CurrentOrderId == null)
            throw new InvalidOperationException("Driver has no active delivery");

        _state = _state with
        {
            Status = DriverStatus.Available,
            CurrentOrderId = null,
            DeliveredToday = _state.DeliveredToday + 1,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Changes driver status (Available, OnBreak, Offline).
    /// </summary>
    public Task<DriverState> UpdateStatusAsync(DriverStatus status, CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Driver {ActorId} not initialized");

        if (_state.Status == DriverStatus.Busy && status != DriverStatus.Busy)
            throw new InvalidOperationException("Cannot change status while on active delivery");

        _state = _state with
        {
            Status = status,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Gets the current driver state.
    /// </summary>
    public Task<DriverState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Checks if driver is available for assignment.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state?.Status == DriverStatus.Available);
    }
}
