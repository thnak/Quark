using Microsoft.Extensions.Logging;
using Quark.Sagas;

namespace Quark.Examples.Sagas;

/// <summary>
/// Saga step that schedules shipment for an order.
/// </summary>
public class ScheduleShipmentStep : ISagaStep<OrderContext>
{
    private readonly ILogger _logger;
    private readonly bool _shouldFail;

    public string Name => "ScheduleShipment";

    public ScheduleShipmentStep(ILogger logger, bool shouldFail = false)
    {
        _logger = logger;
        _shouldFail = shouldFail;
    }

    public async Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scheduling shipment for order {OrderId} to customer {CustomerId}",
            context.OrderId, context.CustomerId);

        // Simulate shipment scheduling
        await Task.Delay(200, cancellationToken);

        if (_shouldFail)
        {
            _logger.LogError("Shipment scheduling failed for order {OrderId}", context.OrderId);
            throw new InvalidOperationException("No carriers available for delivery");
        }

        // Scheduling successful
        context.ShipmentId = Guid.NewGuid().ToString("N")[..12];
        context.ShipmentScheduled = true;

        _logger.LogInformation("Shipment scheduled successfully. Shipment ID: {ShipmentId}",
            context.ShipmentId);
    }

    public async Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Canceling shipment for order {OrderId}, shipment {ShipmentId}",
            context.OrderId, context.ShipmentId);

        // Simulate shipment cancellation
        await Task.Delay(100, cancellationToken);

        context.ShipmentScheduled = false;
        _logger.LogInformation("Shipment canceled successfully for order {OrderId}", context.OrderId);
    }
}
