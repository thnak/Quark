using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that manages an individual chef's workload and cooking tasks.
/// Handles order assignments and tracks cooking progress.
/// </summary>
[Actor(InterfaceType = typeof(IChefActor), Reentrant = false)]
public class ChefActor : ActorBase, IChefActor
{
    private ChefState? _state;

    public ChefActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In full implementation, load state from Redis
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes a new chef.
    /// </summary>
    public Task<ChefState> InitializeAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_state != null)
            throw new InvalidOperationException($"Chef {ActorId} already initialized");

        _state = new ChefState(
            ChefId: ActorId,
            Name: name,
            Status: ChefStatus.Available,
            CurrentOrders: new List<string>());

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Assigns an order to this chef.
    /// </summary>
    public Task<ChefState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_state == null)
            throw new InvalidOperationException($"Chef {ActorId} not initialized");

        if (_state.Status == ChefStatus.OnBreak)
            throw new InvalidOperationException("Chef is on break and cannot accept orders");

        var currentOrders = new List<string>(_state.CurrentOrders) { orderId };

        _state = _state with
        {
            Status = ChefStatus.Busy,
            CurrentOrders = currentOrders
        };

        // TODO: await SaveStateAsync()
        // TODO: Register timer to check cooking progress
        return Task.FromResult(_state);
    }

    public Task<ChefState> CompleteOrderAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Marks an order as complete.
    /// </summary>
    public Task<ChefState> CompleteOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_state == null)
            throw new InvalidOperationException($"Chef {ActorId} not initialized");

        if (!_state.CurrentOrders.Contains(orderId))
            throw new InvalidOperationException($"Order {orderId} is not assigned to this chef");

        var currentOrders = new List<string>(_state.CurrentOrders);
        currentOrders.Remove(orderId);

        var newStatus = currentOrders.Count == 0 ? ChefStatus.Available : ChefStatus.Busy;

        _state = _state with
        {
            Status = newStatus,
            CurrentOrders = currentOrders,
            CompletedToday = _state.CompletedToday + 1
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Changes chef status (Available, OnBreak).
    /// </summary>
    public Task<ChefState> UpdateStatusAsync(ChefStatus status, CancellationToken cancellationToken = default)
    {
        if (_state == null)
            throw new InvalidOperationException($"Chef {ActorId} not initialized");

        if (_state.CurrentOrders.Count > 0 && status == ChefStatus.OnBreak)
            throw new InvalidOperationException("Cannot go on break with active orders");

        _state = _state with
        {
            Status = status
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Gets the current chef state.
    /// </summary>
    public Task<ChefState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Checks if chef is available for new orders.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _state?.Status == ChefStatus.Available || 
            (_state?.Status == ChefStatus.Busy && _state.CurrentOrders.Count < 3));
    }

    /// <summary>
    /// Gets the current workload (number of active orders).
    /// </summary>
    public Task<int> GetWorkloadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state?.CurrentOrders.Count ?? 0);
    }
}
