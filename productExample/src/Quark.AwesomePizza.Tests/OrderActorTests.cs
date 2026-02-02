using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Silo.Actors;

namespace Quark.AwesomePizza.Tests;

/// <summary>
/// Tests for OrderActor functionality
/// </summary>
public class OrderActorTests
{
    [Fact]
    public async Task CreateOrderAsync_WithValidRequest_CreatesOrder()
    {
        // Arrange
        var actor = new OrderActor("test-order-1");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        // Act
        var response = await actor.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("test-order-1", response.OrderId);
        Assert.Equal(OrderStatus.Created, response.State!.Status);
        Assert.Equal("customer-1", response.State.CustomerId);
        Assert.Equal("restaurant-1", response.State.RestaurantId);
        Assert.Equal(12.99m, response.State.TotalAmount);
    }

    [Fact]
    public async Task CreateOrderAsync_CalledTwice_ThrowsException()
    {
        // Arrange
        var actor = new OrderActor("test-order-2");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        await actor.CreateOrderAsync(request);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.CreateOrderAsync(request));
    }

    [Fact]
    public async Task ConfirmOrderAsync_FromCreatedStatus_UpdatesToConfirmed()
    {
        // Arrange
        var actor = new OrderActor("test-order-3");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        await actor.CreateOrderAsync(request);

        // Act
        var order = await actor.ConfirmOrderAsync();

        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithInvalidTransition_ThrowsException()
    {
        // Arrange
        var actor = new OrderActor("test-order-4");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        await actor.CreateOrderAsync(request);

        // Act & Assert
        // Cannot go directly from Created to Delivered
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.UpdateStatusAsync(new UpdateStatusRequest(OrderStatus.Delivered)));
    }

    [Fact]
    public async Task AssignDriverAsync_WhenOrderReady_AssignsDriver()
    {
        // Arrange
        var actor = new OrderActor("test-order-5");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        await actor.CreateOrderAsync(request);
        await actor.ConfirmOrderAsync();
        await actor.UpdateStatusAsync(new UpdateStatusRequest(OrderStatus.Preparing));
        await actor.UpdateStatusAsync(new UpdateStatusRequest(OrderStatus.Baking));
        await actor.UpdateStatusAsync(new UpdateStatusRequest(OrderStatus.Ready));

        // Act
        var order = await actor.AssignDriverAsync("driver-1");

        // Assert
        Assert.Equal(OrderStatus.DriverAssigned, order.Status);
        Assert.Equal("driver-1", order.AssignedDriverId);
    }

    [Fact]
    public async Task GetOrderAsync_AfterCreation_ReturnsOrderState()
    {
        // Arrange
        var actor = new OrderActor("test-order-6");
        await actor.OnActivateAsync();

        var request = new CreateOrderRequest
        {
            CustomerId = "customer-1",
            RestaurantId = "restaurant-1",
            Items = new List<PizzaItem>
            {
                new PizzaItem
                {
                    PizzaType = "Margherita",
                    Size = "Medium",
                    Toppings = new List<string> { "cheese", "tomato" },
                    Quantity = 1,
                    Price = 12.99m
                }
            },
            DeliveryAddress = new GpsLocation(40.7128, -74.0060, DateTime.UtcNow)
        };

        await actor.CreateOrderAsync(request);

        // Act
        var order = await actor.GetOrderAsync();

        // Assert
        Assert.NotNull(order);
        Assert.Equal("test-order-6", order.OrderId);
        Assert.Equal(OrderStatus.Created, order.Status);
    }
}
