namespace Quark.Examples.Sagas;

/// <summary>
/// Context shared across all saga steps during order processing.
/// </summary>
public class OrderContext
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required List<string> Items { get; init; }

    // State flags to track what has been done
    public bool PaymentCompleted { get; set; }
    public bool InventoryReserved { get; set; }
    public bool ShipmentScheduled { get; set; }

    // Transaction IDs for compensation
    public string? PaymentTransactionId { get; set; }
    public string? InventoryReservationId { get; set; }
    public string? ShipmentId { get; set; }
}
