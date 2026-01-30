using System.Collections.Concurrent;
using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Tracks activity metrics for individual actors.
/// </summary>
internal sealed class ActorActivityState
{
    private int _queueDepth;
    private int _activeCallCount;
    private int _streamSubscriptionCount;
    private DateTimeOffset _lastActivityTime = DateTimeOffset.UtcNow;
    private readonly object _lock = new();

    public string ActorId { get; }
    public string ActorType { get; }

    public ActorActivityState(string actorId, string actorType)
    {
        ActorId = actorId;
        ActorType = actorType;
    }

    public void IncrementQueueDepth()
    {
        Interlocked.Increment(ref _queueDepth);
        UpdateLastActivity();
    }

    public void DecrementQueueDepth()
    {
        Interlocked.Decrement(ref _queueDepth);
        UpdateLastActivity();
    }

    public void IncrementActiveCalls()
    {
        Interlocked.Increment(ref _activeCallCount);
        UpdateLastActivity();
    }

    public void DecrementActiveCalls()
    {
        Interlocked.Decrement(ref _activeCallCount);
        UpdateLastActivity();
    }

    public void UpdateStreamSubscription(bool subscribed)
    {
        if (subscribed)
        {
            Interlocked.Increment(ref _streamSubscriptionCount);
        }
        else
        {
            Interlocked.Decrement(ref _streamSubscriptionCount);
        }
        UpdateLastActivity();
    }

    private void UpdateLastActivity()
    {
        lock (_lock)
        {
            _lastActivityTime = DateTimeOffset.UtcNow;
        }
    }

    public ActorActivityMetrics ToMetrics()
    {
        var queueDepth = Volatile.Read(ref _queueDepth);
        var activeCallCount = Volatile.Read(ref _activeCallCount);
        var streamCount = Volatile.Read(ref _streamSubscriptionCount);
        DateTimeOffset lastActivity;

        lock (_lock)
        {
            lastActivity = _lastActivityTime;
        }

        var activityScore = CalculateActivityScore(queueDepth, activeCallCount, streamCount, lastActivity);

        return new ActorActivityMetrics(
            ActorId,
            ActorType,
            queueDepth,
            activeCallCount,
            lastActivity,
            streamCount > 0,
            activityScore);
    }

    private static double CalculateActivityScore(
        int queueDepth,
        int activeCallCount,
        int streamCount,
        DateTimeOffset lastActivity)
    {
        // Calculate time since last activity
        var timeSinceActivity = DateTimeOffset.UtcNow - lastActivity;

        // Base score from queue depth (0-0.4)
        var queueScore = Math.Min(queueDepth / 10.0, 0.4);

        // Active call contribution (0-0.3)
        var callScore = Math.Min(activeCallCount / 5.0, 0.3);

        // Stream subscription contribution (0-0.2)
        var streamScore = streamCount > 0 ? 0.2 : 0.0;

        // Time decay factor (0-0.1) - recent activity adds to score
        var timeScore = timeSinceActivity.TotalSeconds < 60
            ? 0.1 * (1.0 - timeSinceActivity.TotalSeconds / 60.0)
            : 0.0;

        return Math.Min(queueScore + callScore + streamScore + timeScore, 1.0);
    }
}

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
