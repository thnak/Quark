using Microsoft.Extensions.Logging;
using Quark.Sagas;

namespace Quark.Examples.Sagas;

/// <summary>
/// Saga step that reserves inventory for an order.
/// </summary>
public class ReserveInventoryStep : ISagaStep<OrderContext>
{
    private readonly ILogger _logger;
    private readonly bool _shouldFail;

    public string Name => "ReserveInventory";

    public ReserveInventoryStep(ILogger logger, bool shouldFail = false)
    {
        _logger = logger;
        _shouldFail = shouldFail;
    }

    public async Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reserving inventory for order {OrderId}: {Items}",
            context.OrderId, string.Join(", ", context.Items));

        // Simulate inventory check and reservation
        await Task.Delay(150, cancellationToken);

        if (_shouldFail)
        {
            _logger.LogError("Inventory reservation failed for order {OrderId}", context.OrderId);
            throw new InvalidOperationException("Insufficient inventory for requested items");
        }

        // Reservation successful
        context.InventoryReservationId = Guid.NewGuid().ToString("N")[..12];
        context.InventoryReserved = true;

        _logger.LogInformation("Inventory reserved successfully. Reservation ID: {ReservationId}",
            context.InventoryReservationId);
    }

    public async Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Releasing inventory reservation for order {OrderId}, reservation {ReservationId}",
            context.OrderId, context.InventoryReservationId);

        // Simulate inventory release
        await Task.Delay(100, cancellationToken);

        context.InventoryReserved = false;
        _logger.LogInformation("Inventory released successfully for order {OrderId}", context.OrderId);
    }
}
