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