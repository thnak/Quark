namespace Quark.Demo.PizzaDash.Shared.Models;

/// <summary>
/// Represents the status of a pizza order throughout its lifecycle.
/// </summary>
public enum OrderStatus
{
    /// <summary>Initial state when order is placed</summary>
    Ordered,
    
    /// <summary>Chef is preparing the dough and adding toppings</summary>
    PreparingDough,
    
    /// <summary>Pizza is in the oven</summary>
    Baking,
    
    /// <summary>Pizza is ready for delivery</summary>
    ReadyForPickup,
    
    /// <summary>Driver has been assigned</summary>
    DriverAssigned,
    
    /// <summary>Pizza is out for delivery</summary>
    OutForDelivery,
    
    /// <summary>Pizza has been delivered to customer</summary>
    Delivered,
    
    /// <summary>Order was cancelled</summary>
    Cancelled
}

/// <summary>
/// Represents a GPS location with timestamp.
/// </summary>
public record GpsLocation(
    double Latitude,
    double Longitude,
    DateTime Timestamp);

/// <summary>
/// Represents a complete pizza order with all state.
/// </summary>
public record OrderState(
    string OrderId,
    string CustomerId,
    string PizzaType,
    OrderStatus Status,
    DateTime OrderTime,
    DateTime LastUpdated,
    string? DriverId = null,
    GpsLocation? DriverLocation = null,
    string? ETag = null);

/// <summary>
/// Request to create a new order.
/// </summary>
public record CreateOrderRequest(
    string CustomerId,
    string PizzaType);

/// <summary>
/// Request to update order status.
/// </summary>
public record UpdateStatusRequest(
    OrderStatus NewStatus,
    string? DriverId = null);

/// <summary>
/// Status update event for streaming.
/// </summary>
public record OrderStatusUpdate(
    string OrderId,
    OrderStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null);

/// <summary>
/// Represents a kitchen order for chef processing.
/// </summary>
public record KitchenOrder(
    string OrderId,
    string PizzaType,
    DateTime OrderTime);

/// <summary>
/// Chef work assignment.
/// </summary>
public record ChefAssignment(
    string ChefId,
    string OrderId,
    string PizzaType,
    DateTime AssignedAt);
