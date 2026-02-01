using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that manages the state and lifecycle of a pizza order.
/// Demonstrates optimistic concurrency with E-Tag persistence.
/// This is the core actor for the Awesome Pizza system.
/// </summary>
[Actor(InterfaceType = typeof(IOrderActor), Reentrant = false)]
public class OrderActor : ActorBase, IOrderActor
{
    private OrderState? _state;
    private readonly List<Action<OrderStatusUpdate>> _subscribers = new();

    public OrderActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In a full implementation, this would load state from Redis
        // using the IStateStorage<OrderState> interface
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a new pizza order.
    /// </summary>
    public Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_state != null)
            throw new InvalidOperationException($"Order {ActorId} already exists");

        var totalAmount = request.Items.Sum(item => item.Price * item.Quantity);
        var estimatedDeliveryTime = DateTime.UtcNow.AddMinutes(45);

        _state = new OrderState(
            OrderId: ActorId,
            CustomerId: request.CustomerId,
            RestaurantId: request.RestaurantId,
            Items: request.Items,
            Status: OrderStatus.Created,
            CreatedAt: DateTime.UtcNow,
            LastUpdated: DateTime.UtcNow,
            EstimatedDeliveryTime: estimatedDeliveryTime,
            DeliveryAddress: request.DeliveryAddress,
            TotalAmount: totalAmount,
            SpecialInstructions: request.SpecialInstructions,
            ETag: Guid.NewGuid().ToString());

        NotifySubscribers("Order created successfully");

        // TODO: In full implementation:
        // - await SaveStateAsync() with optimistic concurrency
        // - await RegisterReminderAsync("CheckOrderProgress", TimeSpan.FromMinutes(10))
        // - await PublishToStreamAsync("orders/created", new OrderCreatedEvent(...))

        return Task.FromResult(new CreateOrderResponse(
            ActorId,
            _state,
            estimatedDeliveryTime));
    }

    /// <summary>
    /// Updates the order status with optimistic concurrency check.
    /// </summary>
    public Task<OrderState> UpdateStatusAsync(
        UpdateStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        ValidateStatusTransition(_state.Status, request.NewStatus);

        var newETag = Guid.NewGuid().ToString();

        _state = _state with
        {
            Status = request.NewStatus,
            LastUpdated = DateTime.UtcNow,
            AssignedChefId = request.AssignedChefId ?? _state.AssignedChefId,
            AssignedDriverId = request.AssignedDriverId ?? _state.AssignedDriverId,
            ETag = newETag
        };

        NotifySubscribers($"Order status updated to {request.NewStatus}");

        // TODO: In full implementation:
        // - await SaveStateAsync() with E-Tag check
        // - If E-Tag doesn't match, throw ConcurrencyException and retry
        // - await PublishToStreamAsync("orders/status-changed", new StatusChangedEvent(...))

        return Task.FromResult(_state);
    }

    /// <summary>
    /// Confirms the order and sends it to the kitchen.
    /// </summary>
    public async Task<OrderState> ConfirmOrderAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.Created)
            throw new InvalidOperationException("Order can only be confirmed from Created status");

        // TODO: Check inventory availability
        // var inventoryActor = GetActor<InventoryActor>(_state.RestaurantId);
        // await inventoryActor.ReserveIngredientsAsync(_state.Items);

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.Confirmed),
            cancellationToken);
    }

    /// <summary>
    /// Assigns a chef to the order.
    /// </summary>
    public async Task<OrderState> AssignChefAsync(string chefId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chefId);

        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.Confirmed)
            throw new InvalidOperationException("Order must be confirmed before assigning a chef");

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.Preparing, AssignedChefId: chefId),
            cancellationToken);
    }

    /// <summary>
    /// Updates the driver's GPS location.
    /// Called by the MQTT bridge when driver device sends location updates.
    /// </summary>
    public Task UpdateDriverLocationAsync(
        GpsLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.AssignedDriverId == null)
            throw new InvalidOperationException("No driver assigned to this order");

        _state = _state with
        {
            CurrentDriverLocation = location,
            LastUpdated = DateTime.UtcNow
        };

        NotifySubscribers($"Driver location updated: {location.Latitude}, {location.Longitude}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Assigns a driver to the order.
    /// </summary>
    public async Task<OrderState> AssignDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driverId);

        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.Ready)
            throw new InvalidOperationException("Order must be ready before assigning a driver");

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.DriverAssigned, AssignedDriverId: driverId),
            cancellationToken);
    }

    /// <summary>
    /// Marks the order as out for delivery.
    /// </summary>
    public async Task<OrderState> StartDeliveryAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.DriverAssigned)
            throw new InvalidOperationException("Driver must be assigned before starting delivery");

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.OutForDelivery),
            cancellationToken);
    }

    /// <summary>
    /// Marks the order as delivered.
    /// </summary>
    public async Task<OrderState> CompleteDeliveryAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status != OrderStatus.OutForDelivery)
            throw new InvalidOperationException("Order must be out for delivery before completing");

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.Delivered),
            cancellationToken);
    }

    /// <summary>
    /// Cancels the order.
    /// </summary>
    public async Task<OrderState> CancelOrderAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Order {ActorId} does not exist");

        if (_state.Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Cannot cancel a delivered order");

        // TODO: Refund payment, release inventory

        return await UpdateStatusAsync(
            new UpdateStatusRequest(OrderStatus.Cancelled),
            cancellationToken);
    }

    /// <summary>
    /// Gets the current order state.
    /// </summary>
    public Task<OrderState?> GetOrderAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Checks if an order is stuck (taking too long in a particular status).
    /// This would be called by the reminder system.
    /// </summary>
    public Task<bool> IsOrderStuckAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            return Task.FromResult(false);

        var timeSinceUpdate = DateTime.UtcNow - _state.LastUpdated;
        
        return _state.Status switch
        {
            OrderStatus.Baking when timeSinceUpdate.TotalMinutes > 15 => Task.FromResult(true),
            OrderStatus.OutForDelivery when timeSinceUpdate.TotalMinutes > 30 => Task.FromResult(true),
            _ => Task.FromResult(false)
        };
    }

    /// <summary>
    /// Subscribes to real-time status updates.
    /// Used by the Gateway for Server-Sent Events (SSE).
    /// </summary>
    public void Subscribe(Action<OrderStatusUpdate> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _subscribers.Add(callback);
    }

    /// <summary>
    /// Unsubscribes from status updates.
    /// </summary>
    public void Unsubscribe(Action<OrderStatusUpdate> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _subscribers.Remove(callback);
    }

    /// <summary>
    /// Validates that a status transition is allowed.
    /// </summary>
    private static void ValidateStatusTransition(OrderStatus current, OrderStatus next)
    {
        var validTransitions = current switch
        {
            OrderStatus.Created => new[] { OrderStatus.Confirmed, OrderStatus.Cancelled },
            OrderStatus.Confirmed => new[] { OrderStatus.Preparing, OrderStatus.Cancelled },
            OrderStatus.Preparing => new[] { OrderStatus.Baking, OrderStatus.Cancelled },
            OrderStatus.Baking => new[] { OrderStatus.Ready, OrderStatus.Cancelled },
            OrderStatus.Ready => new[] { OrderStatus.DriverAssigned, OrderStatus.Cancelled },
            OrderStatus.DriverAssigned => new[] { OrderStatus.OutForDelivery, OrderStatus.Cancelled },
            OrderStatus.OutForDelivery => new[] { OrderStatus.Delivered },
            OrderStatus.Delivered => Array.Empty<OrderStatus>(),
            OrderStatus.Cancelled => Array.Empty<OrderStatus>(),
            _ => Array.Empty<OrderStatus>()
        };

        if (!validTransitions.Contains(next))
        {
            throw new InvalidOperationException(
                $"Invalid status transition from {current} to {next}");
        }
    }

    /// <summary>
    /// Notifies all subscribers of a status change.
    /// </summary>
    private void NotifySubscribers(string message)
    {
        if (_state == null) return;

        var update = new OrderStatusUpdate(
            _state.OrderId,
            _state.Status,
            DateTime.UtcNow,
            _state.CurrentDriverLocation,
            message);

        foreach (var subscriber in _subscribers)
        {
            try
            {
                subscriber(update);
            }
            catch
            {
                // Ignore subscriber errors to prevent cascading failures
                // In production, log this error
            }
        }
    }
}
