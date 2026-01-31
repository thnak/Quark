# Quark Saga Orchestration Example

This example demonstrates how to use Quark's saga orchestration capabilities to build reliable distributed transactions with automatic compensation.

## What is a Saga?

A saga is a design pattern for managing long-running distributed transactions. Instead of using traditional two-phase commits (which don't scale well), sagas break transactions into a series of local transactions, each with a corresponding compensation action that can undo its effects if something goes wrong.

## The Order Processing Saga

This example implements a typical e-commerce order processing workflow:

1. **Process Payment** - Charge the customer's payment method
2. **Reserve Inventory** - Reserve the ordered items in the warehouse
3. **Schedule Shipment** - Create a shipment for the order

Each step has a compensation action:
- Payment → Refund
- Inventory Reservation → Release
- Shipment → Cancel

## Key Features Demonstrated

### Automatic Compensation

When any step fails, the saga automatically compensates all completed steps in reverse order:

```
Example: Inventory fails after payment succeeds
1. ✅ Process Payment (succeeded)
2. ❌ Reserve Inventory (failed)
3. ⏭️  Schedule Shipment (skipped)

Compensation:
1. 🔄 Refund Payment (compensate in reverse order)
```

### State Persistence

The saga stores its state after each step, allowing it to:
- Resume from the last checkpoint after a crash
- Track which steps have been completed
- Maintain an audit trail for debugging

### Idempotent Operations

All compensation operations are designed to be idempotent, meaning they can be safely retried without causing issues.

## Running the Example

```bash
dotnet run --project examples/Quark.Examples.Sagas/Quark.Examples.Sagas.csproj
```

The example runs three scenarios:

1. **Successful Order** - All steps complete successfully
2. **Payment Failure** - First step fails, no compensation needed
3. **Inventory Failure** - Second step fails, payment is automatically refunded

## Code Structure

- **`OrderContext.cs`** - Shared context passed between saga steps
- **`ProcessPaymentStep.cs`** - Payment processing with refund compensation
- **`ReserveInventoryStep.cs`** - Inventory reservation with release compensation
- **`ScheduleShipmentStep.cs`** - Shipment scheduling with cancellation compensation
- **`OrderProcessingSaga.cs`** - Saga that orchestrates all steps
- **`Program.cs`** - Example scenarios

## Real-World Applications

Sagas are ideal for:

- **E-commerce Order Processing** - Payment, inventory, shipping coordination
- **Travel Booking Systems** - Flight, hotel, car rental reservations
- **Financial Transactions** - Multi-step transfers with rollback capability
- **Approval Workflows** - Multi-stage approval processes with rollback

## Integration with Actors

While this example uses sagas standalone, sagas can be integrated with Quark actors for even more powerful distributed workflows. Each saga step can call actor methods, and actors can coordinate multiple sagas.

## Learn More

- See `docs/ENHANCEMENTS.md` Phase 10.3.1 for the saga orchestration specification
- See `src/Quark.Sagas/` for the implementation details
- See `tests/Quark.Tests/Saga*.cs` for comprehensive test examples
