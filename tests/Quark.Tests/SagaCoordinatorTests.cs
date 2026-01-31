using Microsoft.Extensions.Logging.Abstractions;
using Quark.Sagas;

namespace Quark.Tests;

public class SagaCoordinatorTests
{
    private class TestContext
    {
        public string Data { get; set; } = string.Empty;
    }

    private class TestStep : ISagaStep<TestContext>
    {
        public string Name => "TestStep";

        public Task ExecuteAsync(TestContext context, CancellationToken cancellationToken = default)
        {
            context.Data = "Executed";
            return Task.CompletedTask;
        }

        public Task CompensateAsync(TestContext context, CancellationToken cancellationToken = default)
        {
            context.Data = "Compensated";
            return Task.CompletedTask;
        }
    }

    private class TestSaga : SagaBase<TestContext>
    {
        public TestSaga(string sagaId, ISagaStateStore stateStore)
            : base(sagaId, stateStore, NullLogger.Instance)
        {
            AddStep(new TestStep());
        }
    }

    [Fact]
    public async Task StartSagaAsync_NewSaga_ExecutesSuccessfully()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);
        var saga = new TestSaga("saga-1", stateStore);
        var context = new TestContext();

        // Act
        var status = await coordinator.StartSagaAsync(saga, context);

        // Assert
        Assert.Equal(SagaStatus.Completed, status);
        Assert.Equal("Executed", context.Data);
    }

    [Fact]
    public async Task StartSagaAsync_SagaAlreadyExists_ThrowsException()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);
        var saga1 = new TestSaga("saga-2", stateStore);
        var saga2 = new TestSaga("saga-2", stateStore);
        var context = new TestContext();

        // Start first saga
        await coordinator.StartSagaAsync(saga1, context);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.StartSagaAsync(saga2, context));
    }

    [Fact]
    public async Task ResumeSagaAsync_ExistingSaga_ResumesFromCheckpoint()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);

        // Create a partial saga state
        var partialState = new SagaState
        {
            SagaId = "saga-3",
            Status = SagaStatus.Running,
            CurrentStepIndex = 0,
            StartedAt = DateTimeOffset.UtcNow
        };
        await stateStore.SaveStateAsync(partialState);

        var saga = new TestSaga("saga-3", stateStore);
        var context = new TestContext();

        // Act
        var status = await coordinator.ResumeSagaAsync("saga-3", saga, context);

        // Assert
        Assert.Equal(SagaStatus.Completed, status);
        Assert.Equal("Executed", context.Data);
    }

    [Fact]
    public async Task ResumeSagaAsync_SagaNotFound_ThrowsException()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);
        var saga = new TestSaga("saga-4", stateStore);
        var context = new TestContext();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.ResumeSagaAsync("saga-4", saga, context));
    }

    [Fact]
    public async Task ResumeSagaAsync_CompletedSaga_ReturnsCompletedStatus()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);

        // Create a completed saga state
        var completedState = new SagaState
        {
            SagaId = "saga-5",
            Status = SagaStatus.Completed,
            CurrentStepIndex = 1,
            CompletedSteps = new List<string> { "TestStep" },
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await stateStore.SaveStateAsync(completedState);

        var saga = new TestSaga("saga-5", stateStore);
        var context = new TestContext();

        // Act
        var status = await coordinator.ResumeSagaAsync("saga-5", saga, context);

        // Assert
        Assert.Equal(SagaStatus.Completed, status);
    }

    [Fact]
    public async Task GetSagaStateAsync_ExistingSaga_ReturnsState()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);
        
        var state = new SagaState
        {
            SagaId = "saga-6",
            Status = SagaStatus.Running,
            CurrentStepIndex = 1,
            StartedAt = DateTimeOffset.UtcNow
        };
        await stateStore.SaveStateAsync(state);

        // Act
        var retrievedState = await coordinator.GetSagaStateAsync("saga-6");

        // Assert
        Assert.NotNull(retrievedState);
        Assert.Equal("saga-6", retrievedState.SagaId);
        Assert.Equal(SagaStatus.Running, retrievedState.Status);
        Assert.Equal(1, retrievedState.CurrentStepIndex);
    }

    [Fact]
    public async Task GetSagaStateAsync_NonExistentSaga_ReturnsNull()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);

        // Act
        var state = await coordinator.GetSagaStateAsync("non-existent");

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task RecoverInProgressSagasAsync_FindsRunningSagas()
    {
        // Arrange
        var stateStore = new InMemorySagaStateStore();
        var coordinator = new SagaCoordinator<TestContext>(stateStore, NullLogger<SagaCoordinator<TestContext>>.Instance);

        // Create some sagas in different states
        await stateStore.SaveStateAsync(new SagaState
        {
            SagaId = "saga-7",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        await stateStore.SaveStateAsync(new SagaState
        {
            SagaId = "saga-8",
            Status = SagaStatus.Compensating,
            StartedAt = DateTimeOffset.UtcNow
        });

        await stateStore.SaveStateAsync(new SagaState
        {
            SagaId = "saga-9",
            Status = SagaStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        // Act
        var count = await coordinator.RecoverInProgressSagasAsync();

        // Assert
        Assert.Equal(2, count);  // Should find saga-7 and saga-8
    }
}
