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

/// <summary>
/// Tracks actor activity levels for hot/cold actor detection during migration.
/// Part of Phase 10.1.1 (Zero Downtime and Rolling Upgrades - Live Actor Migration).
/// </summary>
public interface IActorActivityTracker
{
    /// <summary>
    /// Records an actor message enqueued event.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    void RecordMessageEnqueued(string actorId, string actorType);

    /// <summary>
    /// Records an actor message dequeued event.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    void RecordMessageDequeued(string actorId, string actorType);

    /// <summary>
    /// Records an actor call started event.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    void RecordCallStarted(string actorId, string actorType);

    /// <summary>
    /// Records an actor call completed event.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    void RecordCallCompleted(string actorId, string actorType);

    /// <summary>
    /// Records an actor stream subscription event.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="subscribed">True if subscribed, false if unsubscribed.</param>
    void RecordStreamActivity(string actorId, string actorType, bool subscribed);

    /// <summary>
    /// Gets the activity metrics for a specific actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The activity metrics for the actor, or null if the actor is not tracked.</returns>
    Task<ActorActivityMetrics?> GetActivityMetricsAsync(
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets activity metrics for all tracked actors on this silo.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of activity metrics for all tracked actors.</returns>
    Task<IReadOnlyCollection<ActorActivityMetrics>> GetAllActivityMetricsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets activity metrics sorted by migration priority (cold actors first).
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of activity metrics sorted by migration priority.</returns>
    Task<IReadOnlyCollection<ActorActivityMetrics>> GetMigrationPriorityListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes tracking data for an actor (when deactivated).
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    void RemoveActor(string actorId);
}
