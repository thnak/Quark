namespace Quark.Abstractions.Migration;

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
