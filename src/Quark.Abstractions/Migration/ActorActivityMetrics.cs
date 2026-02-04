namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents metrics about an actor's activity level.
/// </summary>
public sealed class ActorActivityMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorActivityMetrics"/> class.
    /// </summary>
    public ActorActivityMetrics(
        string actorId,
        string actorType,
        int queueDepth,
        int activeCallCount,
        DateTimeOffset lastActivityTime,
        bool hasActiveStreams,
        double activityScore)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        QueueDepth = queueDepth;
        ActiveCallCount = activeCallCount;
        LastActivityTime = lastActivityTime;
        HasActiveStreams = hasActiveStreams;
        ActivityScore = activityScore;
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the current message queue depth.
    /// </summary>
    public int QueueDepth { get; }

    /// <summary>
    /// Gets the number of currently active calls.
    /// </summary>
    public int ActiveCallCount { get; }

    /// <summary>
    /// Gets the timestamp of the last activity.
    /// </summary>
    public DateTimeOffset LastActivityTime { get; }

    /// <summary>
    /// Gets whether the actor has active stream subscriptions.
    /// </summary>
    public bool HasActiveStreams { get; }

    /// <summary>
    /// Gets the activity score (0.0 to 1.0, higher means more active/hot).
    /// Used for priority-based migration ordering (cold actors migrate first).
    /// </summary>
    public double ActivityScore { get; }

    /// <summary>
    /// Gets whether this actor is considered "hot" (actively processing messages).
    /// </summary>
    public bool IsHot => ActivityScore > 0.5 || ActiveCallCount > 0 || QueueDepth > 0;

    /// <summary>
    /// Gets whether this actor is considered "cold" (idle or minimal activity).
    /// </summary>
    public bool IsCold => !IsHot;
}