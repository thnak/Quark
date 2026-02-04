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