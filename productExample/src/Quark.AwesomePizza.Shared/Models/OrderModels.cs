namespace Quark.AwesomePizza.Shared.Models;

/// <summary>
/// Represents the status of a pizza order throughout its lifecycle.
/// </summary>
public enum OrderStatus
{
    /// <summary>Initial state when order is placed</summary>
    Created,
    
    /// <summary>Order is confirmed and sent to kitchen</summary>
    Confirmed,
    
    /// <summary>Chef is preparing the dough and adding toppings</summary>
    Preparing,
    
    /// <summary>Pizza is in the oven</summary>
    Baking,
    
    /// <summary>Pizza is ready for delivery</summary>
    Ready,
    
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
    DateTime Timestamp,
    double? Accuracy = null);

/// <summary>
/// Represents a pizza item in an order.
/// </summary>
public record PizzaItem(
    string PizzaType,
    string Size,
    List<string> Toppings,
    int Quantity,
    decimal Price);

/// <summary>
/// Represents the complete state of an order.
/// </summary>
public record OrderState(
    string OrderId,
    string CustomerId,
    string RestaurantId,
    List<PizzaItem> Items,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime LastUpdated,
    DateTime? EstimatedDeliveryTime = null,
    string? AssignedChefId = null,
    string? AssignedDriverId = null,
    GpsLocation? DeliveryAddress = null,
    GpsLocation? CurrentDriverLocation = null,
    decimal TotalAmount = 0,
    string? SpecialInstructions = null,
    string? ETag = null);

/// <summary>
/// Request to create a new order.
/// </summary>
public record CreateOrderRequest(
    string CustomerId,
    string RestaurantId,
    List<PizzaItem> Items,
    GpsLocation DeliveryAddress,
    string? SpecialInstructions = null);

/// <summary>
/// Response after creating an order.
/// </summary>
public record CreateOrderResponse(
    string OrderId,
    OrderState State,
    DateTime EstimatedDeliveryTime);

/// <summary>
/// Request to update order status.
/// </summary>
public record UpdateStatusRequest(
    OrderStatus NewStatus,
    string? AssignedChefId = null,
    string? AssignedDriverId = null);

/// <summary>
/// Order status update event for streaming.
/// </summary>
public record OrderStatusUpdate(
    string OrderId,
    OrderStatus Status,
    DateTime Timestamp,
    GpsLocation? DriverLocation = null,
    string? Message = null);

/// <summary>
/// Driver availability status.
/// </summary>
public enum DriverStatus
{
    Available,
    Busy,
    OnBreak,
    Offline
}

/// <summary>
/// Represents driver state.
/// </summary>
public record DriverState(
    string DriverId,
    string Name,
    DriverStatus Status,
    GpsLocation? CurrentLocation = null,
    string? CurrentOrderId = null,
    DateTime LastUpdated = default,
    int DeliveredToday = 0);

/// <summary>
/// Request to update driver location via MQTT.
/// </summary>
public record UpdateDriverLocationRequest(
    string DriverId,
    double Latitude,
    double Longitude,
    DateTime Timestamp);

/// <summary>
/// Chef status.
/// </summary>
public enum ChefStatus
{
    Available,
    Busy,
    OnBreak
}

/// <summary>
/// Represents chef state.
/// </summary>
public record ChefState(
    string ChefId,
    string Name,
    ChefStatus Status,
    List<string> CurrentOrders,
    int CompletedToday = 0);

/// <summary>
/// Inventory item.
/// </summary>
public record InventoryItem(
    string ItemId,
    string Name,
    decimal Quantity,
    string Unit,
    decimal LowStockThreshold,
    decimal ReorderQuantity);

/// <summary>
/// Inventory state for a restaurant.
/// </summary>
public record InventoryState(
    string RestaurantId,
    Dictionary<string, InventoryItem> Items,
    DateTime LastUpdated);

/// <summary>
/// Kitchen queue entry.
/// </summary>
public record KitchenQueueItem(
    string OrderId,
    List<PizzaItem> Items,
    DateTime OrderTime,
    string? AssignedChefId = null,
    DateTime? EstimatedCompletionTime = null);

/// <summary>
/// Kitchen state.
/// </summary>
public record KitchenState(
    string KitchenId,
    string RestaurantId,
    List<KitchenQueueItem> Queue,
    List<string> AvailableChefs,
    int OrdersCompletedToday = 0);

/// <summary>
/// Restaurant metrics.
/// </summary>
public record RestaurantMetrics(
    string RestaurantId,
    int ActiveOrders,
    int CompletedOrders,
    int AvailableDrivers,
    int BusyDrivers,
    int AvailableChefs,
    int BusyChefs,
    decimal AverageDeliveryTime,
    DateTime LastUpdated);
