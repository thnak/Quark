namespace Quark.EventSourcing;

/// <summary>
///     Interface for storing and retrieving domain events (event sourcing).
/// </summary>
public interface IEventStore
{
    /// <summary>
    ///     Appends one or more events to an actor's event stream.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="expectedVersion">
    ///     The expected version (sequence number) before appending.
    ///     Used for optimistic concurrency control. Pass null to skip version check.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new version number after appending the events.</returns>
    /// <exception cref="EventStoreConcurrencyException">Thrown when the expected version doesn't match.</exception>
    Task<long> AppendEventsAsync(
        string actorId,
        IReadOnlyList<DomainEvent> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads all events for an actor from a specific version onward.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="fromVersion">The starting version (inclusive). Use 0 to read from the beginning.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>List of events in sequence order.</returns>
    Task<IReadOnlyList<DomainEvent>> ReadEventsAsync(
        string actorId,
        long fromVersion = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current version (highest sequence number) for an actor's event stream.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current version, or 0 if no events exist.</returns>
    Task<long> GetCurrentVersionAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a snapshot of an actor's state at a specific version.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="snapshot">The snapshot data.</param>
    /// <param name="version">The version at which the snapshot was taken.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SaveSnapshotAsync(
        string actorId,
        object snapshot,
        long version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads the most recent snapshot for an actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The snapshot and its version, or null if no snapshot exists.</returns>
    Task<(object Snapshot, long Version)?> LoadSnapshotAsync(
        string actorId,
        CancellationToken cancellationToken = default);
}
