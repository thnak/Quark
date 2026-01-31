using Quark.Sagas;

namespace Quark.Tests;

public class InMemorySagaStateStoreTests
{
    [Fact]
    public async Task SaveStateAsync_NewState_SavesSuccessfully()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        var state = new SagaState
        {
            SagaId = "saga-1",
            Status = SagaStatus.Running,
            CurrentStepIndex = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        await store.SaveStateAsync(state);

        // Assert
        var loaded = await store.LoadStateAsync("saga-1");
        Assert.NotNull(loaded);
        Assert.Equal("saga-1", loaded.SagaId);
        Assert.Equal(SagaStatus.Running, loaded.Status);
    }

    [Fact]
    public async Task SaveStateAsync_UpdateExisting_OverwritesPrevious()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        var state1 = new SagaState
        {
            SagaId = "saga-2",
            Status = SagaStatus.Running,
            CurrentStepIndex = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        var state2 = new SagaState
        {
            SagaId = "saga-2",
            Status = SagaStatus.Completed,
            CurrentStepIndex = 3,
            StartedAt = state1.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Act
        await store.SaveStateAsync(state1);
        await store.SaveStateAsync(state2);

        // Assert
        var loaded = await store.LoadStateAsync("saga-2");
        Assert.NotNull(loaded);
        Assert.Equal(SagaStatus.Completed, loaded.Status);
        Assert.Equal(3, loaded.CurrentStepIndex);
    }

    [Fact]
    public async Task SaveStateAsync_IsolatesState_MutationsDoNotAffectStored()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        var state = new SagaState
        {
            SagaId = "saga-3",
            Status = SagaStatus.Running,
            CurrentStepIndex = 0,
            CompletedSteps = new List<string> { "Step1" },
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        await store.SaveStateAsync(state);
        state.CompletedSteps.Add("Step2");  // Mutate after saving

        // Assert
        var loaded = await store.LoadStateAsync("saga-3");
        Assert.NotNull(loaded);
        Assert.Single(loaded.CompletedSteps);  // Should not include "Step2"
        Assert.Equal("Step1", loaded.CompletedSteps[0]);
    }

    [Fact]
    public async Task LoadStateAsync_NonExistentSaga_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySagaStateStore();

        // Act
        var loaded = await store.LoadStateAsync("non-existent");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadStateAsync_ReturnsCopy_MutationsDoNotAffectStore()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        var state = new SagaState
        {
            SagaId = "saga-4",
            Status = SagaStatus.Running,
            CurrentStepIndex = 0,
            CompletedSteps = new List<string> { "Step1" },
            StartedAt = DateTimeOffset.UtcNow
        };
        await store.SaveStateAsync(state);

        // Act
        var loaded1 = await store.LoadStateAsync("saga-4");
        loaded1!.CompletedSteps.Add("Step2");  // Mutate the loaded copy
        var loaded2 = await store.LoadStateAsync("saga-4");

        // Assert
        Assert.Single(loaded2!.CompletedSteps);  // Should still be single
    }

    [Fact]
    public async Task DeleteStateAsync_ExistingSaga_RemovesState()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        var state = new SagaState
        {
            SagaId = "saga-5",
            Status = SagaStatus.Completed,
            CurrentStepIndex = 2,
            StartedAt = DateTimeOffset.UtcNow
        };
        await store.SaveStateAsync(state);

        // Act
        await store.DeleteStateAsync("saga-5");

        // Assert
        var loaded = await store.LoadStateAsync("saga-5");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteStateAsync_NonExistentSaga_DoesNotThrow()
    {
        // Arrange
        var store = new InMemorySagaStateStore();

        // Act & Assert - Should not throw
        await store.DeleteStateAsync("non-existent");
    }

    [Fact]
    public async Task GetSagasByStatusAsync_FiltersCorrectly()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        
        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-6",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-7",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-8",
            Status = SagaStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-9",
            Status = SagaStatus.Compensating,
            StartedAt = DateTimeOffset.UtcNow
        });

        // Act
        var runningSagas = await store.GetSagasByStatusAsync(SagaStatus.Running);
        var completedSagas = await store.GetSagasByStatusAsync(SagaStatus.Completed);
        var compensatingSagas = await store.GetSagasByStatusAsync(SagaStatus.Compensating);

        // Assert
        Assert.Equal(2, runningSagas.Count);
        Assert.Single(completedSagas);
        Assert.Single(compensatingSagas);
        
        Assert.Contains(runningSagas, s => s.SagaId == "saga-6");
        Assert.Contains(runningSagas, s => s.SagaId == "saga-7");
        Assert.Contains(completedSagas, s => s.SagaId == "saga-8");
        Assert.Contains(compensatingSagas, s => s.SagaId == "saga-9");
    }

    [Fact]
    public async Task GetSagasByStatusAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        
        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-10",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        // Act
        var compensatedSagas = await store.GetSagasByStatusAsync(SagaStatus.Compensated);

        // Assert
        Assert.Empty(compensatedSagas);
    }

    [Fact]
    public async Task GetSagasByStatusAsync_ReturnsCopies_MutationsDoNotAffectStore()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        
        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-11",
            Status = SagaStatus.Running,
            CompletedSteps = new List<string> { "Step1" },
            StartedAt = DateTimeOffset.UtcNow
        });

        // Act
        var sagas = await store.GetSagasByStatusAsync(SagaStatus.Running);
        sagas[0].CompletedSteps.Add("Step2");  // Mutate the returned copy
        
        var reloaded = await store.LoadStateAsync("saga-11");

        // Assert
        Assert.Single(reloaded!.CompletedSteps);  // Should still be single
    }

    [Fact]
    public void Clear_RemovesAllStates()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        
        store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-12",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        }).Wait();

        store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-13",
            Status = SagaStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow
        }).Wait();

        // Act
        store.Clear();

        // Assert
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Count_ReturnsCorrectNumber()
    {
        // Arrange
        var store = new InMemorySagaStateStore();
        
        // Act & Assert
        Assert.Equal(0, store.Count);

        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-14",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(1, store.Count);

        await store.SaveStateAsync(new SagaState
        {
            SagaId = "saga-15",
            Status = SagaStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(2, store.Count);

        await store.DeleteStateAsync("saga-14");
        Assert.Equal(1, store.Count);
    }
}
