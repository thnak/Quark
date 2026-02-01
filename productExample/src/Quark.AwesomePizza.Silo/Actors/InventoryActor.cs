using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Interfaces;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that manages inventory for a restaurant.
/// Tracks ingredient stock levels and triggers reorder reminders.
/// </summary>
[Actor(InterfaceType = typeof(IInventoryActor), Reentrant = false)]
public class InventoryActor : ActorBase, IInventoryActor
{
    private InventoryState? _state;

    public InventoryActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In full implementation, load state from Redis
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes inventory for a restaurant with default items.
    /// </summary>
    public Task<InventoryState> InitializeAsync(
        string restaurantId,
        Dictionary<string, InventoryItem> initialItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restaurantId);
        ArgumentNullException.ThrowIfNull(initialItems);

        if (_state != null)
            throw new InvalidOperationException($"Inventory for restaurant {restaurantId} already initialized");

        _state = new InventoryState(
            RestaurantId: restaurantId,
            Items: initialItems,
            LastUpdated: DateTime.UtcNow);

        // TODO: await SaveStateAsync()
        // TODO: Register reminders for low stock alerts
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Checks if ingredients are available for an order.
    /// </summary>
    public Task<bool> CheckAvailabilityAsync(
        List<PizzaItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (_state == null)
            throw new InvalidOperationException("Inventory not initialized");

        // Calculate required ingredients
        var requiredIngredients = CalculateRequiredIngredients(items);

        // Check if all ingredients are available
        foreach (var (ingredientId, requiredQuantity) in requiredIngredients)
        {
            if (!_state.Items.TryGetValue(ingredientId, out var item))
                return Task.FromResult(false);

            if (item.Quantity < requiredQuantity)
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Reserves ingredients for an order (decrements stock).
    /// </summary>
    public Task<InventoryState> ReserveIngredientsAsync(
        List<PizzaItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (_state == null)
            throw new InvalidOperationException("Inventory not initialized");

        var requiredIngredients = CalculateRequiredIngredients(items);
        var updatedItems = new Dictionary<string, InventoryItem>(_state.Items);

        foreach (var (ingredientId, requiredQuantity) in requiredIngredients)
        {
            if (!updatedItems.TryGetValue(ingredientId, out var item))
                throw new InvalidOperationException($"Ingredient {ingredientId} not found");

            if (item.Quantity < requiredQuantity)
                throw new InvalidOperationException($"Insufficient {item.Name}: have {item.Quantity}, need {requiredQuantity}");

            var newQuantity = item.Quantity - requiredQuantity;
            updatedItems[ingredientId] = item with { Quantity = newQuantity };

            // Check if low stock threshold reached
            if (newQuantity <= item.LowStockThreshold)
            {
                // TODO: Trigger low stock alert via reminder
                // await RegisterReminderAsync($"LowStock_{ingredientId}", TimeSpan.FromMinutes(1));
            }
        }

        _state = _state with
        {
            Items = updatedItems,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Restocks an ingredient.
    /// </summary>
    public Task<InventoryState> RestockAsync(
        string ingredientId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingredientId);

        if (_state == null)
            throw new InvalidOperationException("Inventory not initialized");

        if (!_state.Items.TryGetValue(ingredientId, out var item))
            throw new InvalidOperationException($"Ingredient {ingredientId} not found");

        var updatedItems = new Dictionary<string, InventoryItem>(_state.Items)
        {
            [ingredientId] = item with { Quantity = item.Quantity + quantity }
        };

        _state = _state with
        {
            Items = updatedItems,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        // TODO: Cancel low stock reminder if stock is now above threshold
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Updates an inventory item's configuration.
    /// </summary>
    public Task<InventoryState> UpdateItemAsync(
        string ingredientId,
        InventoryItem updatedItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingredientId);
        ArgumentNullException.ThrowIfNull(updatedItem);

        if (_state == null)
            throw new InvalidOperationException("Inventory not initialized");

        if (!_state.Items.ContainsKey(ingredientId))
            throw new InvalidOperationException($"Ingredient {ingredientId} not found");

        var updatedItems = new Dictionary<string, InventoryItem>(_state.Items)
        {
            [ingredientId] = updatedItem
        };

        _state = _state with
        {
            Items = updatedItems,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Gets the current inventory state.
    /// </summary>
    public Task<InventoryState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state);
    }

    /// <summary>
    /// Gets items below low stock threshold.
    /// </summary>
    public Task<List<InventoryItem>> GetLowStockItemsAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            return Task.FromResult(new List<InventoryItem>());

        var lowStockItems = _state.Items.Values
            .Where(item => item.Quantity <= item.LowStockThreshold)
            .ToList();

        return Task.FromResult(lowStockItems);
    }

    /// <summary>
    /// Calculates required ingredients based on pizza items.
    /// This is a simplified version - in production, you'd have a recipe database.
    /// </summary>
    private static Dictionary<string, decimal> CalculateRequiredIngredients(List<PizzaItem> items)
    {
        var required = new Dictionary<string, decimal>();

        foreach (var item in items)
        {
            // Simplified ingredient calculation
            // Base ingredients per pizza
            AddIngredient(required, "dough", 1.0m * item.Quantity);
            AddIngredient(required, "sauce", 0.2m * item.Quantity);
            AddIngredient(required, "cheese", 0.3m * item.Quantity);

            // Size multiplier
            var sizeMultiplier = item.Size.ToLower() switch
            {
                "small" => 0.7m,
                "medium" => 1.0m,
                "large" => 1.3m,
                "xlarge" => 1.6m,
                _ => 1.0m
            };

            // Toppings
            foreach (var topping in item.Toppings)
            {
                AddIngredient(required, topping.ToLower(), 0.1m * sizeMultiplier * item.Quantity);
            }
        }

        return required;
    }

    private static void AddIngredient(Dictionary<string, decimal> dict, string key, decimal amount)
    {
        if (dict.ContainsKey(key))
            dict[key] += amount;
        else
            dict[key] = amount;
    }
}
