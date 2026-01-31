using System.Collections.Concurrent;

namespace Quark.Sagas;

/// <summary>
/// In-memory implementation of saga state store.
/// Suitable for development, testing, and single-node scenarios.
/// </summary>
public class InMemorySagaStateStore : ISagaStateStore
{
    private readonly ConcurrentDictionary<string, SagaState> _states = new();

    /// <inheritdoc />
    public Task SaveStateAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        // Deep copy to avoid external mutations
        var stateCopy = new SagaState
        {
            SagaId = state.SagaId,
            Status = state.Status,
            CurrentStepIndex = state.CurrentStepIndex,
            CompletedSteps = new List<string>(state.CompletedSteps),
            CompensatedSteps = new List<string>(state.CompensatedSteps),
            FailureReason = state.FailureReason,
            StartedAt = state.StartedAt,
            CompletedAt = state.CompletedAt,
            Metadata = new Dictionary<string, string>(state.Metadata)
        };

        _states[state.SagaId] = stateCopy;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SagaState?> LoadStateAsync(string sagaId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sagaId))
            throw new ArgumentException("Saga ID cannot be null or empty", nameof(sagaId));

        if (_states.TryGetValue(sagaId, out var state))
        {
            // Return a copy to avoid external mutations
            var stateCopy = new SagaState
            {
                SagaId = state.SagaId,
                Status = state.Status,
                CurrentStepIndex = state.CurrentStepIndex,
                CompletedSteps = new List<string>(state.CompletedSteps),
                CompensatedSteps = new List<string>(state.CompensatedSteps),
                FailureReason = state.FailureReason,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                Metadata = new Dictionary<string, string>(state.Metadata)
            };
            return Task.FromResult<SagaState?>(stateCopy);
        }

        return Task.FromResult<SagaState?>(null);
    }

    /// <inheritdoc />
    public Task DeleteStateAsync(string sagaId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sagaId))
            throw new ArgumentException("Saga ID cannot be null or empty", nameof(sagaId));

        _states.TryRemove(sagaId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SagaState>> GetSagasByStatusAsync(
        SagaStatus status,
        CancellationToken cancellationToken = default)
    {
        var matchingSagas = _states.Values
            .Where(s => s.Status == status)
            .Select(s => new SagaState
            {
                SagaId = s.SagaId,
                Status = s.Status,
                CurrentStepIndex = s.CurrentStepIndex,
                CompletedSteps = new List<string>(s.CompletedSteps),
                CompensatedSteps = new List<string>(s.CompensatedSteps),
                FailureReason = s.FailureReason,
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt,
                Metadata = new Dictionary<string, string>(s.Metadata)
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SagaState>>(matchingSagas);
    }

    /// <summary>
    /// Clears all stored saga states. Useful for testing.
    /// </summary>
    public void Clear()
    {
        _states.Clear();
    }

    /// <summary>
    /// Gets the total number of stored saga states.
    /// </summary>
    public int Count => _states.Count;
}
