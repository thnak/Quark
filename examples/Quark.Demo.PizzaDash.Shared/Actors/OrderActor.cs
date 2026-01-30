using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

namespace Quark.Demo.PizzaDash.Shared.Actors;

/// <summary>
/// Actor that manages the state and lifecycle of a pizza order.
/// Demonstrates optimistic concurrency with E-Tag persistence.
/// </summary>
[Actor(Name = "Order", Reentrant = false)]
public class OrderActor : ActorBase
{
    private OrderState? _state;
    private readonly List<Action<OrderStatusUpdate>> _subscribers = new();

    public OrderActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would load state from Redis
        // For now, we'll initialize empty state
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a new pizza order.
    /// </summary>
    public Task<OrderState> CreateOrderAsync(string customerId, string pizzaType)
    {
        if (_state != null)
            throw new InvalidOperationException($"Order {ActorId} already exists");

        _state = new OrderState(
            OrderId: ActorId,
            CustomerId: customerId,
            PizzaType: pizzaType,
            Status: OrderStatus.Ordered,
            OrderTime: DateTime.UtcNow,
            LastUpdated: DateTime.UtcNow,
            ETag: Guid.NewGuid().ToString());

        NotifySubscribers();
        
        // In real implementation: await SaveStateAsync() with optimistic concurrency
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Updates the order status with optimistic concurrency check.
    /// </summary>
    public Task<OrderState> UpdateStatusAsync(OrderStatus newStatus, string? driverId = null)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        var oldETag = _state.ETag;
        var newETag = Guid.NewGuid().ToString();

        _state = _state with
        {
            Status = newStatus,
            LastUpdated = DateTime.UtcNow,
            DriverId = driverId ?? _state.DriverId,
            ETag = newETag
        };

        NotifySubscribers();
        
        // In real implementation: await SaveStateAsync() with E-Tag check
        // If E-Tag doesn't match, throw ConcurrencyException
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Updates the driver's GPS location.
    /// </summary>
    public Task<OrderState> UpdateDriverLocationAsync(GpsLocation location)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        _state = _state with
        {
            DriverLocation = location,
            LastUpdated = DateTime.UtcNow
        };

        NotifySubscribers();
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Assigns a driver to the order.
    /// </summary>
    public Task<OrderState> AssignDriverAsync(string driverId)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.ReadyForPickup)
            throw new InvalidOperationException("Order must be ready for pickup before assigning driver");

        return UpdateStatusAsync(OrderStatus.DriverAssigned, driverId);
    }

    /// <summary>
    /// Gets the current order state.
    /// </summary>
    public Task<OrderState?> GetOrderAsync()
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Checks if an order is late (in oven for more than 15 minutes).
    /// This would be called by the DeliveryReminder system.
    /// </summary>
    public Task<bool> IsOrderLateAsync()
    {
        if (_state == null || _state.Status != OrderStatus.Baking)
            return Task.FromResult(false);

        var timeSinceUpdate = DateTime.UtcNow - _state.LastUpdated;
        return Task.FromResult(timeSinceUpdate.TotalMinutes > 15);
    }

    /// <summary>
    /// Subscribes to real-time status updates.
    /// </summary>
    public void Subscribe(Action<OrderStatusUpdate> callback)
    {
        _subscribers.Add(callback);
    }

    /// <summary>
    /// Unsubscribes from status updates.
    /// </summary>
    public void Unsubscribe(Action<OrderStatusUpdate> callback)
    {
        _subscribers.Remove(callback);
    }

    private void NotifySubscribers()
    {
        if (_state == null) return;

        var update = new OrderStatusUpdate(
            _state.OrderId,
            _state.Status,
            DateTime.UtcNow,
            _state.DriverLocation);

        foreach (var subscriber in _subscribers)
        {
            try
            {
                subscriber(update);
            }
            catch
            {
                // Ignore subscriber errors to prevent cascading failures
            }
        }
    }
}
