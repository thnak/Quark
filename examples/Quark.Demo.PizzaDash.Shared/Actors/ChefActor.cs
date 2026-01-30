using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Demo.PizzaDash.Shared.Models;

namespace Quark.Demo.PizzaDash.Shared.Actors;

/// <summary>
/// Stateless worker actor that processes incoming pizza orders from the kitchen stream.
/// Demonstrates implicit stream subscriptions with [QuarkStream] attribute.
/// </summary>
[Actor(Name = "Chef", Reentrant = true)]
public class ChefActor : ActorBase
{
    private readonly List<string> _activeOrders = new();

    public ChefActor(string actorId) : base(actorId)
    {
    }

    /// <summary>
    /// Processes a new kitchen order.
    /// In a real system, this would be called automatically via stream subscription.
    /// </summary>
    public Task ProcessOrderAsync(KitchenOrder order)
    {
        _activeOrders.Add(order.OrderId);
        
        // Simulate chef processing
        Console.WriteLine($"[Chef {ActorId}] Processing order {order.OrderId} - {order.PizzaType}");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks an order as completed by this chef.
    /// </summary>
    public Task CompleteOrderAsync(string orderId)
    {
        _activeOrders.Remove(orderId);
        Console.WriteLine($"[Chef {ActorId}] Completed order {orderId}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the list of active orders being processed by this chef.
    /// </summary>
    public Task<IReadOnlyList<string>> GetActiveOrdersAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(_activeOrders.AsReadOnly());
    }

    /// <summary>
    /// Gets the current workload (number of active orders).
    /// </summary>
    public Task<int> GetWorkloadAsync()
    {
        return Task.FromResult(_activeOrders.Count);
    }
}
