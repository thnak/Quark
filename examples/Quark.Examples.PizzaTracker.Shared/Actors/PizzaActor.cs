using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Shared.Actors;

/// <summary>
/// Actor that manages the state and lifecycle of a pizza order.
/// </summary>
[Actor(Name = "Pizza", Reentrant = false)]
public class PizzaActor : ActorBase
{
    private PizzaOrder? _order;
    private readonly List<Action<PizzaStatusUpdate>> _subscribers = new();

    public PizzaActor(string actorId) : base(actorId)
    {
    }

    /// <summary>
    /// Creates a new pizza order.
    /// </summary>
    public Task<PizzaOrder> CreateOrderAsync(string customerId, string pizzaType)
    {
        _order = new PizzaOrder(
            OrderId: ActorId,
            CustomerId: customerId,
            PizzaType: pizzaType,
            Status: PizzaStatus.Ordered,
            OrderTime: DateTime.UtcNow);

        NotifySubscribers();
        return Task.FromResult(_order);
    }

    /// <summary>
    /// Updates the status of the pizza.
    /// </summary>
    public Task<PizzaOrder> UpdateStatusAsync(PizzaStatus newStatus, string? driverId = null)
    {
        if (_order == null)
            throw new InvalidOperationException("No order exists for this pizza actor");

        _order = _order with { Status = newStatus, DriverId = driverId ?? _order.DriverId };
        NotifySubscribers();
        return Task.FromResult(_order);
    }

    /// <summary>
    /// Updates the driver's GPS location.
    /// </summary>
    public Task<PizzaOrder> UpdateDriverLocationAsync(GpsLocation location)
    {
        if (_order == null)
            throw new InvalidOperationException("No order exists for this pizza actor");

        _order = _order with { DriverLocation = location };
        NotifySubscribers();
        return Task.FromResult(_order);
    }

    /// <summary>
    /// Gets the current order state.
    /// </summary>
    public Task<PizzaOrder?> GetOrderAsync()
    {
        return Task.FromResult(_order);
    }

    /// <summary>
    /// Subscribes to status updates.
    /// </summary>
    public void Subscribe(Action<PizzaStatusUpdate> callback)
    {
        _subscribers.Add(callback);
    }

    private void NotifySubscribers()
    {
        if (_order == null) return;

        var update = new PizzaStatusUpdate(
            _order.OrderId,
            _order.Status,
            DateTime.UtcNow,
            _order.DriverLocation);

        foreach (var subscriber in _subscribers)
        {
            try
            {
                subscriber(update);
            }
            catch
            {
                // Ignore subscriber errors
            }
        }
    }
}
