using Microsoft.Extensions.Logging;
using Quark.Sagas;

namespace Quark.Examples.Sagas;

/// <summary>
/// Order processing saga that coordinates payment, inventory, and shipment.
/// </summary>
public class OrderProcessingSaga : SagaBase<OrderContext>
{
    public OrderProcessingSaga(string orderId, ISagaStateStore stateStore, ILogger logger, string? failAtStep = null)
        : base(orderId, stateStore, logger)
    {
        // Add saga steps in the order they should be executed
        AddStep(new ProcessPaymentStep(logger, failAtStep == "ProcessPayment"));
        AddStep(new ReserveInventoryStep(logger, failAtStep == "ReserveInventory"));
        AddStep(new ScheduleShipmentStep(logger, failAtStep == "ScheduleShipment"));
    }
}
