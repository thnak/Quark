using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that manages kitchen operations and order queue.
/// Demonstrates supervision pattern by managing ChefActors.
/// </summary>
[Actor(InterfaceType = typeof(IKitchenActor), Reentrant = false)]
public class KitchenActor : ActorBase, IKitchenActor
{
    private KitchenState? _state;

    public KitchenActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In full implementation, load state from Redis
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes a new kitchen.
    /// </summary>
    public Task<KitchenState> InitializeAsync(
        string restaurantId,
        List<string> chefIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restaurantId);
        ArgumentNullException.ThrowIfNull(chefIds);

        if (_state != null)
            throw new InvalidOperationException($"Kitchen {ActorId} already initialized");

        _state = new KitchenState(
            KitchenId: ActorId,
            RestaurantId: restaurantId,
            Queue: new List<KitchenQueueItem>(),
            AvailableChefs: chefIds);

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Adds an order to the kitchen queue.
    /// </summary>
    public async Task<KitchenQueueItem> AddToQueueAsync(
        string orderId,
        List<PizzaItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(items);

        if (_state == null)
            throw new InvalidOperationException($"Kitchen {ActorId} not initialized");

        var queueItem = new KitchenQueueItem(
            OrderId: orderId,
            Items: items,
            OrderTime: DateTime.UtcNow);

        var queue = new List<KitchenQueueItem>(_state.Queue) { queueItem };

        _state = _state with { Queue = queue };

        // TODO: await SaveStateAsync()
        
        // Try to assign to an available chef immediately
        await TryAssignChefAsync(orderId, cancellationToken);

        return queueItem;
    }

    /// <summary>
    /// Tries to assign an available chef to an order.
    /// Uses load balancing to distribute work evenly.
    /// </summary>
    public async Task<bool> TryAssignChefAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_state == null)
            throw new InvalidOperationException($"Kitchen {ActorId} not initialized");

        var queueItem = _state.Queue.FirstOrDefault(q => q.OrderId == orderId);
        if (queueItem == null || queueItem.AssignedChefId != null)
            return false;

        // Find chef with lowest workload
        string? bestChefId = null;
        int lowestWorkload = int.MaxValue;

        foreach (var chefId in _state.AvailableChefs)
        {
            // TODO: Get ChefActor and check workload
            // var chefActor = GetActor<ChefActor>(chefId);
            // var workload = await chefActor.GetWorkloadAsync(cancellationToken);
            // if (workload < lowestWorkload)
            // {
            //     lowestWorkload = workload;
            //     bestChefId = chefId;
            // }
            
            // For now, just assign to first available chef
            bestChefId = chefId;
            break;
        }

        if (bestChefId == null)
            return false;

        // Assign the order to the chef
        var estimatedCompletionTime = DateTime.UtcNow.AddMinutes(15); // 15 min cooking time

        var updatedItem = queueItem with
        {
            AssignedChefId = bestChefId,
            EstimatedCompletionTime = estimatedCompletionTime
        };

        var queue = new List<KitchenQueueItem>(_state.Queue);
        var index = queue.FindIndex(q => q.OrderId == orderId);
        queue[index] = updatedItem;

        _state = _state with { Queue = queue };

        // TODO: await SaveStateAsync()
        // TODO: Call ChefActor.AssignOrderAsync(orderId)
        // TODO: Call OrderActor.AssignChefAsync(bestChefId)

        return true;
    }

    /// <summary>
    /// Marks an order as complete and removes it from the queue.
    /// </summary>
    public async Task<KitchenState> CompleteOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_state == null)
            throw new InvalidOperationException($"Kitchen {ActorId} not initialized");

        var queueItem = _state.Queue.FirstOrDefault(q => q.OrderId == orderId);
        if (queueItem == null)
            throw new InvalidOperationException($"Order {orderId} not found in kitchen queue");

        // Remove from queue
        var queue = new List<KitchenQueueItem>(_state.Queue);
        queue.RemoveAll(q => q.OrderId == orderId);

        _state = _state with
        {
            Queue = queue,
            OrdersCompletedToday = _state.OrdersCompletedToday + 1
        };

        // TODO: await SaveStateAsync()
        
        // Notify chef that order is complete
        if (queueItem.AssignedChefId != null)
        {
            // TODO: var chefActor = GetActor<ChefActor>(queueItem.AssignedChefId);
            // TODO: await chefActor.CompleteOrderAsync(orderId, cancellationToken);
        }

        // Try to assign next order in queue to this chef
        var nextOrder = _state.Queue.FirstOrDefault(q => q.AssignedChefId == null);
        if (nextOrder != null)
        {
            await TryAssignChefAsync(nextOrder.OrderId, cancellationToken);
        }

        return _state;
    }

    /// <summary>
    /// Gets the current kitchen queue.
    /// </summary>
    public Task<List<KitchenQueueItem>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state?.Queue ?? new List<KitchenQueueItem>());
    }

    /// <summary>
    /// Gets the current kitchen state.
    /// </summary>
    public Task<KitchenState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Gets queue statistics.
    /// </summary>
    public Task<(int Waiting, int InProgress, int Completed)> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_state == null)
            return Task.FromResult((0, 0, 0));

        var waiting = _state.Queue.Count(q => q.AssignedChefId == null);
        var inProgress = _state.Queue.Count(q => q.AssignedChefId != null);
        var completed = _state.OrdersCompletedToday;

        return Task.FromResult((waiting, inProgress, completed));
    }
}
