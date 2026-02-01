using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Actors;

namespace Quark.AwesomePizza.Silo;

/// <summary>
/// Actor Service for accessing actors from external clients (like Gateway).
/// In a distributed setup, this would use gRPC or other remoting.
/// For now, we use in-process access.
/// </summary>
public interface IActorService
{
    Task<T?> GetActorAsync<T>(string actorId) where T : IActor;
    Task<OrderActor?> GetOrderActorAsync(string orderId);
    Task<DriverActor?> GetDriverActorAsync(string driverId);
    Task<ChefActor?> GetChefActorAsync(string chefId);
    Task<KitchenActor?> GetKitchenActorAsync(string kitchenId);
    Task<InventoryActor?> GetInventoryActorAsync(string inventoryId);
    Task<RestaurantActor?> GetRestaurantActorAsync(string restaurantId);
}

/// <summary>
/// Implementation of the Actor Service.
/// This provides a way for Gateway to access actors in the Silo.
/// </summary>
public class ActorService : IActorService
{
    private readonly IActorFactory _actorFactory;
    private readonly Dictionary<string, IActor> _activeActors;

    public ActorService(IActorFactory actorFactory, Dictionary<string, IActor> activeActors)
    {
        ArgumentNullException.ThrowIfNull(actorFactory);
        ArgumentNullException.ThrowIfNull(activeActors);

        _actorFactory = actorFactory;
        _activeActors = activeActors;
    }

    public async Task<T?> GetActorAsync<T>(string actorId) where T : IActor
    {
        ArgumentNullException.ThrowIfNull(actorId);

        if (_activeActors.TryGetValue(actorId, out var existingActor) && existingActor is T typedActor)
        {
            return typedActor;
        }

        var actor = _actorFactory.CreateActor<T>(actorId);
        await actor.OnActivateAsync();
        _activeActors[actorId] = actor;

        return actor;
    }

    public Task<OrderActor?> GetOrderActorAsync(string orderId) 
        => GetActorAsync<OrderActor>(orderId);

    public Task<DriverActor?> GetDriverActorAsync(string driverId) 
        => GetActorAsync<DriverActor>(driverId);

    public Task<ChefActor?> GetChefActorAsync(string chefId) 
        => GetActorAsync<ChefActor>(chefId);

    public Task<KitchenActor?> GetKitchenActorAsync(string kitchenId) 
        => GetActorAsync<KitchenActor>(kitchenId);

    public Task<InventoryActor?> GetInventoryActorAsync(string inventoryId) 
        => GetActorAsync<InventoryActor>(inventoryId);

    public Task<RestaurantActor?> GetRestaurantActorAsync(string restaurantId) 
        => GetActorAsync<RestaurantActor>(restaurantId);
}
