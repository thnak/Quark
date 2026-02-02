using ProtoBuf;
using Quark.AwesomePizza.Shared.Models;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests to verify ProtoBuf serialization works correctly for records, structs, and classes.
/// </summary>
public class ProtoBufSerializationTests
{
    [Fact]
    public void CreateOrderRequest_SerializesAndDeserializes()
    {
        // Arrange
        var original = new CreateOrderRequest(
            CustomerId: "customer-1",
            RestaurantId: "restaurant-1",
            Items: new List<PizzaItem>
            {
                new PizzaItem("Margherita", "Large", new List<string> { "Cheese", "Tomato" }, 2, 15.99m)
            },
            DeliveryAddress: new GpsLocation(37.7749, -122.4194, DateTime.UtcNow, 10.0),
            SpecialInstructions: "Ring doorbell"
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<CreateOrderRequest>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.CustomerId, deserialized.CustomerId);
        Assert.Equal(original.RestaurantId, deserialized.RestaurantId);
        Assert.Single(deserialized.Items);
        Assert.Equal(original.Items[0].PizzaType, deserialized.Items[0].PizzaType);
        Assert.Equal(original.SpecialInstructions, deserialized.SpecialInstructions);
    }

    [Fact]
    public void OrderState_SerializesAndDeserializes()
    {
        // Arrange
        var original = new OrderState(
            OrderId: "order-1",
            CustomerId: "customer-1",
            RestaurantId: "restaurant-1",
            Items: new List<PizzaItem>
            {
                new PizzaItem("Pepperoni", "Medium", new List<string> { "Cheese", "Pepperoni" }, 1, 12.99m)
            },
            Status: OrderStatus.Created,
            CreatedAt: DateTime.UtcNow,
            LastUpdated: DateTime.UtcNow,
            EstimatedDeliveryTime: DateTime.UtcNow.AddMinutes(30),
            TotalAmount: 12.99m,
            ETag: "v1"
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<OrderState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.OrderId, deserialized.OrderId);
        Assert.Equal(original.CustomerId, deserialized.CustomerId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.TotalAmount, deserialized.TotalAmount);
    }

    [Fact]
    public void GpsLocation_SerializesAndDeserializes()
    {
        // Arrange
        var original = new GpsLocation(
            Latitude: 37.7749,
            Longitude: -122.4194,
            Timestamp: DateTime.UtcNow,
            Accuracy: 5.0
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<GpsLocation>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Latitude, deserialized.Latitude);
        Assert.Equal(original.Longitude, deserialized.Longitude);
        Assert.Equal(original.Accuracy, deserialized.Accuracy);
    }

    [Fact]
    public void PizzaItem_SerializesAndDeserializes()
    {
        // Arrange
        var original = new PizzaItem(
            PizzaType: "Hawaiian",
            Size: "Large",
            Toppings: new List<string> { "Ham", "Pineapple", "Cheese" },
            Quantity: 3,
            Price: 18.99m
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<PizzaItem>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.PizzaType, deserialized.PizzaType);
        Assert.Equal(original.Size, deserialized.Size);
        Assert.Equal(original.Quantity, deserialized.Quantity);
        Assert.Equal(original.Price, deserialized.Price);
        Assert.Equal(3, deserialized.Toppings.Count);
    }

    [Fact]
    public void ChefState_SerializesAndDeserializes()
    {
        // Arrange
        var original = new ChefState(
            ChefId: "chef-1",
            Name: "Gordon Ramsay",
            Status: ChefStatus.Available,
            CurrentOrders: new List<string> { "order-1", "order-2" },
            CompletedToday: 15
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<ChefState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.ChefId, deserialized.ChefId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(2, deserialized.CurrentOrders.Count);
        Assert.Equal(original.CompletedToday, deserialized.CompletedToday);
    }

    [Fact]
    public void DriverState_SerializesAndDeserializes()
    {
        // Arrange
        var original = new DriverState(
            DriverId: "driver-1",
            Name: "Fast Eddie",
            Status: DriverStatus.Available,
            CurrentLocation: new GpsLocation(37.7749, -122.4194, DateTime.UtcNow),
            CurrentOrderId: "order-1",
            LastUpdated: DateTime.UtcNow,
            DeliveredToday: 8
        );

        // Act
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var deserialized = Serializer.Deserialize<DriverState>(ms);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.DriverId, deserialized.DriverId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.NotNull(deserialized.CurrentLocation);
        Assert.Equal(original.DeliveredToday, deserialized.DeliveredToday);
    }

    [Fact]
    public void AllRecordTypes_CanBeSerializedAndDeserialized()
    {
        // Test CreateOrderRequest
        {
            var original = new CreateOrderRequest("c1", "r1", new List<PizzaItem>(), 
                new GpsLocation(0, 0, DateTime.UtcNow));
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<CreateOrderRequest>(ms);
            Assert.NotNull(deserialized);
        }

        // Test CreateOrderResponse
        {
            var original = new CreateOrderResponse("o1", 
                new OrderState("o1", "c1", "r1", new List<PizzaItem>(), 
                    OrderStatus.Created, DateTime.UtcNow, DateTime.UtcNow), 
                DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<CreateOrderResponse>(ms);
            Assert.NotNull(deserialized);
        }

        // Test UpdateStatusRequest
        {
            var original = new UpdateStatusRequest(OrderStatus.Confirmed);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<UpdateStatusRequest>(ms);
            Assert.NotNull(deserialized);
        }

        // Test OrderStatusUpdate
        {
            var original = new OrderStatusUpdate("o1", OrderStatus.Created, DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<OrderStatusUpdate>(ms);
            Assert.NotNull(deserialized);
        }

        // Test UpdateDriverLocationRequest
        {
            var original = new UpdateDriverLocationRequest("d1", 0, 0, DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<UpdateDriverLocationRequest>(ms);
            Assert.NotNull(deserialized);
        }

        // Test KitchenState
        {
            var original = new KitchenState("k1", "r1", new List<KitchenQueueItem>(), new List<string>());
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<KitchenState>(ms);
            Assert.NotNull(deserialized);
        }

        // Test KitchenQueueItem
        {
            var original = new KitchenQueueItem("o1", new List<PizzaItem>(), DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<KitchenQueueItem>(ms);
            Assert.NotNull(deserialized);
        }

        // Test InventoryState
        {
            var original = new InventoryState("r1", new Dictionary<string, InventoryItem>(), DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<InventoryState>(ms);
            Assert.NotNull(deserialized);
        }

        // Test InventoryItem
        {
            var original = new InventoryItem("i1", "Cheese", 100m, "kg", 10m, 50m);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<InventoryItem>(ms);
            Assert.NotNull(deserialized);
        }

        // Test RestaurantMetrics
        {
            var original = new RestaurantMetrics("r1", 5, 20, 3, 1, 4, 2, 25.5m, DateTime.UtcNow);
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<RestaurantMetrics>(ms);
            Assert.NotNull(deserialized);
        }
    }
}
