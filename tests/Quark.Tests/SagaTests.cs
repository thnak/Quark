using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Sagas;

namespace Quark.Tests;

public class SagaTests
{
    // Test context to share data between saga steps
    private class OrderContext
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool PaymentProcessed { get; set; }
        public bool InventoryReserved { get; set; }
        public bool ShipmentScheduled { get; set; }
        public List<string> ExecutionLog { get; set; } = new();
    }

    // Test saga step for payment processing
    private class PaymentStep : ISagaStep<OrderContext>
    {
        private readonly bool _shouldFail;

        public string Name => "ProcessPayment";

        public PaymentStep(bool shouldFail = false)
        {
            _shouldFail = shouldFail;
        }

        public Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            if (_shouldFail)
                throw new InvalidOperationException("Payment processing failed");

            context.PaymentProcessed = true;
            context.ExecutionLog.Add($"Executed: {Name}");
            return Task.CompletedTask;
        }

        public Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            context.PaymentProcessed = false;
            context.ExecutionLog.Add($"Compensated: {Name}");
            return Task.CompletedTask;
        }
    }

    // Test saga step for inventory reservation
    private class InventoryStep : ISagaStep<OrderContext>
    {
        private readonly bool _shouldFail;

        public string Name => "ReserveInventory";

        public InventoryStep(bool shouldFail = false)
        {
            _shouldFail = shouldFail;
        }

        public Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            if (_shouldFail)
                throw new InvalidOperationException("Inventory reservation failed");

            context.InventoryReserved = true;
            context.ExecutionLog.Add($"Executed: {Name}");
            return Task.CompletedTask;
        }

        public Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            context.InventoryReserved = false;
            context.ExecutionLog.Add($"Compensated: {Name}");
            return Task.CompletedTask;
        }
    }

    // Test saga step for shipment scheduling
    private class ShipmentStep : ISagaStep<OrderContext>
    {
        private readonly bool _shouldFail;

        public string Name => "ScheduleShipment";

        public ShipmentStep(bool shouldFail = false)
        {
            _shouldFail = shouldFail;
        }

        public Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            if (_shouldFail)
                throw new InvalidOperationException("Shipment scheduling failed");

            context.ShipmentScheduled = true;
            context.ExecutionLog.Add($"Executed: {Name}");
            return Task.CompletedTask;
        }

        public Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
        {
            context.ShipmentScheduled = false;
            context.ExecutionLog.Add($"Compensated: {Name}");
            return Task.CompletedTask;
        }
    }

    // Test saga implementation
    private class OrderSaga : SagaBase<OrderContext>
    {
        public OrderSaga(string sagaId, ISagaStateStore stateStore, ILogger logger)
            : base(sagaId, stateStore, logger)
        {
        }

        public void AddSteps(params ISagaStep<OrderContext>[] steps)
        {
            foreach (var step in steps)
            {
                AddStep(step);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_CompletesSuccessfully()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var saga = new OrderSaga("order-123", stateStore, NullLogger.Instance);
        saga.AddSteps(
            new PaymentStep(),
            new InventoryStep(),
            new ShipmentStep()
        );

        var context = new OrderContext
        {
            OrderId = "order-123",
            Amount = 100.00m
        };

        // Act
        var status = await saga.ExecuteAsync(context);

        // Assert
        Assert.Equal(SagaStatus.Completed, status);
        Assert.True(context.PaymentProcessed);
        Assert.True(context.InventoryReserved);
        Assert.True(context.ShipmentScheduled);
        Assert.Equal(3, context.ExecutionLog.Count);
        Assert.Contains("Executed: ProcessPayment", context.ExecutionLog);
        Assert.Contains("Executed: ReserveInventory", context.ExecutionLog);
        Assert.Contains("Executed: ScheduleShipment", context.ExecutionLog);
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_CompensatesCompletedSteps()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var saga = new OrderSaga("order-456", stateStore, NullLogger.Instance);
        saga.AddSteps(
            new PaymentStep(),
            new InventoryStep(shouldFail: true),  // This step will fail
            new ShipmentStep()
        );

        var context = new OrderContext
        {
            OrderId = "order-456",
            Amount = 100.00m
        };

        // Act
        var status = await saga.ExecuteAsync(context);

        // Assert
        Assert.Equal(SagaStatus.Compensated, status);
        Assert.False(context.PaymentProcessed);  // Should be compensated
        Assert.False(context.InventoryReserved); // Failed step
        Assert.False(context.ShipmentScheduled); // Never executed
        
        // Check execution log
        Assert.Contains("Executed: ProcessPayment", context.ExecutionLog);
        Assert.Contains("Compensated: ProcessPayment", context.ExecutionLog);
        Assert.DoesNotContain("Executed: ScheduleShipment", context.ExecutionLog);
    }

    [Fact]
    public async Task ExecuteAsync_SavesStateAfterEachStep()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var saga = new OrderSaga("order-789", stateStore, NullLogger.Instance);
        saga.AddSteps(
            new PaymentStep(),
            new InventoryStep(),
            new ShipmentStep()
        );

        var context = new OrderContext { OrderId = "order-789", Amount = 100.00m };

        // Act
        await saga.ExecuteAsync(context);

        // Assert
        var savedState = await stateStore.LoadStateAsync("order-789");
        Assert.NotNull(savedState);
        Assert.Equal(SagaStatus.Completed, savedState.Status);
        Assert.Equal(3, savedState.CompletedSteps.Count);
        Assert.Contains("ProcessPayment", savedState.CompletedSteps);
        Assert.Contains("ReserveInventory", savedState.CompletedSteps);
        Assert.Contains("ScheduleShipment", savedState.CompletedSteps);
    }

    [Fact]
    public async Task ExecuteAsync_ResumesFromCheckpoint()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        
        // First execution that completes some steps
        var saga1 = new OrderSaga("order-resume", stateStore, NullLogger.Instance);
        saga1.AddSteps(
            new PaymentStep(),
            new InventoryStep(),
            new ShipmentStep()
        );

        var context = new OrderContext { OrderId = "order-resume", Amount = 100.00m };

        // Simulate partial completion by manually setting state
        var partialState = new SagaState
        {
            SagaId = "order-resume",
            Status = SagaStatus.Running,
            CurrentStepIndex = 2,  // Resume from step 2 (ShipmentStep)
            CompletedSteps = new List<string> { "ProcessPayment", "ReserveInventory" },
            StartedAt = DateTimeOffset.UtcNow
        };
        await stateStore.SaveStateAsync(partialState);

        // Act - Resume execution
        var status = await saga1.ExecuteAsync(context);

        // Assert
        Assert.Equal(SagaStatus.Completed, status);
        Assert.True(context.ShipmentScheduled);  // Only last step should execute in context
        
        var finalState = await stateStore.LoadStateAsync("order-resume");
        Assert.NotNull(finalState);
        Assert.Equal(3, finalState.CompletedSteps.Count);
        Assert.Contains("ScheduleShipment", finalState.CompletedSteps);
    }

    [Fact]
    public async Task CompensateAsync_CompensatesInReverseOrder()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var saga = new OrderSaga("order-comp", stateStore, NullLogger.Instance);
        saga.AddSteps(
            new PaymentStep(),
            new InventoryStep(),
            new ShipmentStep()
        );

        var context = new OrderContext { OrderId = "order-comp", Amount = 100.00m };

        // Complete all steps first
        await saga.ExecuteAsync(context);
        
        // Clear log to track compensation order
        context.ExecutionLog.Clear();

        // Act
        await saga.CompensateAsync(context);

        // Assert
        Assert.Equal(SagaStatus.Compensated, saga.State.Status);
        Assert.Equal(3, context.ExecutionLog.Count);
        
        // Verify reverse order
        Assert.Equal("Compensated: ScheduleShipment", context.ExecutionLog[0]);
        Assert.Equal("Compensated: ReserveInventory", context.ExecutionLog[1]);
        Assert.Equal("Compensated: ProcessPayment", context.ExecutionLog[2]);
    }

    [Fact]
    public async Task SagaState_TracksFailureReason()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var saga = new OrderSaga("order-fail", stateStore, NullLogger.Instance);
        saga.AddSteps(
            new PaymentStep(),
            new InventoryStep(shouldFail: true)
        );

        var context = new OrderContext { OrderId = "order-fail", Amount = 100.00m };

        // Act
        await saga.ExecuteAsync(context);

        // Assert
        var state = await stateStore.LoadStateAsync("order-fail");
        Assert.NotNull(state);
        Assert.NotNull(state.FailureReason);
        Assert.Contains("ReserveInventory", state.FailureReason);
        Assert.Contains("failed", state.FailureReason);
    }
}
