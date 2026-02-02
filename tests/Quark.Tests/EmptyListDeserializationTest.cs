using ProtoBuf;
using Quark.AwesomePizza.Shared.Models;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests to verify that empty lists in ProtoBuf messages are preserved during deserialization.
/// This addresses the issue where empty lists were becoming null, causing ArgumentNullException.
/// </summary>
public class EmptyListDeserializationTest
{
    [Fact]
    public void CreateOrderRequest_WithEmptyItems_PreservesEmptyList()
    {
        // Arrange - Create request with empty Items list
        var original = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>(),  // Empty list
            DeliveryAddress = new GpsLocation(37.7749, -122.4194, DateTime.UtcNow)
        };

        // Act - Serialize and deserialize
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<CreateOrderRequest>(ms);

        // Assert - Items should still be an empty list, not null
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Items);  // This was failing before the fix
        Assert.Empty(deserialized.Items);
        
        // Verify we can safely use LINQ methods without null check
        Assert.Equal(0, deserialized.Items.Count);
        Assert.False(deserialized.Items.Any());
    }

    [Fact]
    public void PizzaItem_WithEmptyToppings_PreservesEmptyList()
    {
        // Arrange
        var original = new PizzaItem
        {
            PizzaType = "Plain",
            Size = "Medium",
            Toppings = new List<string>(),  // Empty list
            Quantity = 1,
            Price = 10.99m
        };

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<PizzaItem>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Toppings);  // This was failing before the fix
        Assert.Empty(deserialized.Toppings);
    }

    [Fact]
    public void ChefState_WithEmptyCurrentOrders_PreservesEmptyList()
    {
        // Arrange
        var original = new ChefState
        {
            ChefId = "chef-1",
            Name = "Test Chef",
            Status = ChefStatus.Available,
            CurrentOrders = new List<string>(),  // Empty list
            CompletedToday = 0
        };

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<ChefState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.CurrentOrders);  // This was failing before the fix
        Assert.Empty(deserialized.CurrentOrders);
    }

    [Fact]
    public void OrderState_WithEmptyItems_PreservesEmptyList()
    {
        // Arrange
        var original = new OrderState
        {
            OrderId = "order-1",
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>(),  // Empty list
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<OrderState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Items);  // This was failing before the fix
        Assert.Empty(deserialized.Items);
    }

    [Fact]
    public void KitchenState_WithEmptyQueues_PreservesEmptyLists()
    {
        // Arrange
        var original = new KitchenState
        {
            KitchenId = "kitchen-1",
            RestaurantId = "restaurant-1",
            Queue = new List<KitchenQueueItem>(),  // Empty list
            AvailableChefs = new List<string>()  // Empty list
        };

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<KitchenState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Queue);  // This was failing before the fix
        Assert.NotNull(deserialized.AvailableChefs);  // This was failing before the fix
        Assert.Empty(deserialized.Queue);
        Assert.Empty(deserialized.AvailableChefs);
    }
}
