using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Order Actor - manages pizza order lifecycle.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IOrderActor
{
    /// <summary>
    /// Creates a new pizza order.
    /// </summary>
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current order state.
    /// </summary>
    Task<OrderState?> GetOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the order status.
    /// </summary>
    Task<OrderState> UpdateStatusAsync(UpdateStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the order (sends to kitchen).
    /// </summary>
    Task<OrderState> ConfirmOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a driver to the order.
    /// </summary>
    Task<OrderState> AssignDriverAsync(string driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts delivery (driver picked up the order).
    /// </summary>
    Task<OrderState> StartDeliveryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes delivery (marks as delivered).
    /// </summary>
    Task<OrderState> CompleteDeliveryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order.
    /// </summary>
    Task<OrderState> CancelOrderAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver location on the order.
    /// </summary>
    Task UpdateDriverLocationAsync(GpsLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to order status updates.
    /// </summary>
    void Subscribe(Action<OrderStatusUpdate> callback);

    /// <summary>
    /// Unsubscribes from order status updates.
    /// </summary>
    void Unsubscribe(Action<OrderStatusUpdate> callback);
}
