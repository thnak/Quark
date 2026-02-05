using Quark.Abstractions;
using Quark.Abstractions.Converters;
using Quark.AwesomePizza.Shared.Converters;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Order Actor - manages pizza order lifecycle.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IOrderActor : IQuarkActor
{
    /// <summary>
    /// Creates a new pizza order.
    /// </summary>
    [BinaryConverter(typeof(CreateOrderRequestConverter), ParameterName = "request", Order = 0)]
    [BinaryConverter(typeof(CreateOrderResponseConverter))]
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current order state.
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    Task<OrderState?> GetOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the order status.
    /// </summary>
    [BinaryConverter(typeof(UpdateStatusRequestConverter), ParameterName = "request", Order = 0)]
    [BinaryConverter(typeof(OrderStateConverter))]
    Task<OrderState> UpdateStatusAsync(UpdateStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the order (sends to kitchen).
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    Task<OrderState> ConfirmOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a driver to the order.
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    [BinaryConverter(typeof(StringConverter), ParameterName = "driverId", Order = 0)]
    Task<OrderState> AssignDriverAsync(string driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts delivery (driver picked up the order).
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    Task<OrderState> StartDeliveryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes delivery (marks as delivered).
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    Task<OrderState> CompleteDeliveryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order.
    /// </summary>
    [BinaryConverter(typeof(OrderStateConverter))]
    [BinaryConverter(typeof(StringConverter), ParameterName = "reason", Order = 0)]
    Task<OrderState> CancelOrderAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates driver location on the order.
    /// </summary>
    [BinaryConverter(typeof(GpsLocationConverter), ParameterName = "location", Order = 0)]
    Task UpdateDriverLocationAsync(GpsLocation location, CancellationToken cancellationToken = default);

    // /// <summary>
    // /// Subscribes to order status updates.
    // /// </summary>
    // void Subscribe(Action<OrderStatusUpdate> callback);

    // /// <summary>
    // /// Unsubscribes from order status updates.
    // /// </summary>
    // void Unsubscribe(Action<OrderStatusUpdate> callback);
}