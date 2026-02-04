using System.Collections.Concurrent;
using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Default implementation of IActorActivityTracker.
/// Tracks actor activity for hot/cold detection during migration.
/// </summary>
public sealed class ActorActivityTracker : IActorActivityTracker
{
    private readonly ConcurrentDictionary<string, ActorActivityState> _actorStates = new();

    /// <inheritdoc />
    public void RecordMessageEnqueued(string actorId, string actorType)
    {
        var state = GetOrCreateState(actorId, actorType);
        state.IncrementQueueDepth();
    }

    /// <inheritdoc />
    public void RecordMessageDequeued(string actorId, string actorType)
    {
        var state = GetOrCreateState(actorId, actorType);
        state.DecrementQueueDepth();
    }

    /// <inheritdoc />
    public void RecordCallStarted(string actorId, string actorType)
    {
        var state = GetOrCreateState(actorId, actorType);
        state.IncrementActiveCalls();
    }

    /// <inheritdoc />
    public void RecordCallCompleted(string actorId, string actorType)
    {
        var state = GetOrCreateState(actorId, actorType);
        state.DecrementActiveCalls();
    }

    /// <inheritdoc />
    public void RecordStreamActivity(string actorId, string actorType, bool subscribed)
    {
        var state = GetOrCreateState(actorId, actorType);
        state.UpdateStreamSubscription(subscribed);
    }

    /// <inheritdoc />
    public Task<ActorActivityMetrics?> GetActivityMetricsAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (_actorStates.TryGetValue(actorId, out var state))
        {
            return Task.FromResult<ActorActivityMetrics?>(state.ToMetrics());
        }

        return Task.FromResult<ActorActivityMetrics?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ActorActivityMetrics>> GetAllActivityMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var metrics = _actorStates.Values
            .Select(state => state.ToMetrics())
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ActorActivityMetrics>>(metrics);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ActorActivityMetrics>> GetMigrationPriorityListAsync(
        CancellationToken cancellationToken = default)
    {
        // Sort by activity score ascending (cold actors first for migration priority)
        var metrics = _actorStates.Values
            .Select(state => state.ToMetrics())
            .OrderBy(m => m.ActivityScore)
            .ThenBy(m => m.QueueDepth)
            .ThenBy(m => m.ActiveCallCount)
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ActorActivityMetrics>>(metrics);
    }

    /// <inheritdoc />
    public void RemoveActor(string actorId)
    {
        _actorStates.TryRemove(actorId, out _);
    }

    private ActorActivityState GetOrCreateState(string actorId, string actorType)
    {
        return _actorStates.GetOrAdd(actorId, _ => new ActorActivityState(actorId, actorType));
    }
}
