using System.Collections.Concurrent;

namespace Quark.DurableTasks;

/// <summary>
///     In-memory implementation of orchestration state store for development and testing.
/// </summary>
public sealed class InMemoryOrchestrationStateStore : IOrchestrationStateStore
{
    private readonly ConcurrentDictionary<string, OrchestrationState> _states = new();

    /// <inheritdoc />
    public Task SaveStateAsync(OrchestrationState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states[state.OrchestrationId] = state;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrchestrationState?> LoadStateAsync(string orchestrationId, CancellationToken cancellationToken = default)
    {
        _states.TryGetValue(orchestrationId, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task DeleteStateAsync(string orchestrationId, CancellationToken cancellationToken = default)
    {
        _states.TryRemove(orchestrationId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the total number of orchestrations (for testing).
    /// </summary>
    public int Count => _states.Count;

    /// <summary>
    ///     Clears all state (for testing).
    /// </summary>
    public void Clear() => _states.Clear();
}
