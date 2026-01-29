namespace Quark.EventSourcing;

/// <summary>
///     Base class for event-sourced actors that rebuild state from events.
/// </summary>
public abstract class EventSourcedActor
{
    private readonly IEventStore _eventStore;
    private readonly List<DomainEvent> _uncommittedEvents = new();
    private long _version;

    /// <summary>
    ///     Gets the current version of the actor's event stream.
    /// </summary>
    protected long Version => _version;

    /// <summary>
    ///     Gets the actor identifier.
    /// </summary>
    protected abstract string ActorId { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventSourcedActor"/> class.
    /// </summary>
    /// <param name="eventStore">The event store for persisting events.</param>
    protected EventSourcedActor(IEventStore eventStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    /// <summary>
    ///     Loads the actor's state by replaying all events from the event store.
    /// </summary>
    protected async Task RecoverStateAsync(CancellationToken cancellationToken = default)
    {
        // Try to load snapshot first
        var snapshot = await _eventStore.LoadSnapshotAsync(ActorId, cancellationToken);
        var fromVersion = 0L;

        if (snapshot.HasValue)
        {
            ApplySnapshot(snapshot.Value.Snapshot);
            fromVersion = snapshot.Value.Version + 1;
            _version = snapshot.Value.Version;
        }

        // Replay events from snapshot version onward
        var events = await _eventStore.ReadEventsAsync(ActorId, fromVersion, cancellationToken);
        foreach (var @event in events)
        {
            Apply(@event);
            _version = @event.SequenceNumber;
        }
    }

    /// <summary>
    ///     Raises a new event, applying it to the actor's state and marking it for persistence.
    /// </summary>
    /// <param name="event">The event to raise.</param>
    protected void RaiseEvent(DomainEvent @event)
    {
        @event.ActorId = ActorId;
        @event.Timestamp = DateTimeOffset.UtcNow;
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    /// <summary>
    ///     Persists all uncommitted events to the event store.
    /// </summary>
    protected async Task CommitEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_uncommittedEvents.Count == 0)
            return;

        var newVersion = await _eventStore.AppendEventsAsync(
            ActorId,
            _uncommittedEvents,
            _version,
            cancellationToken);

        _version = newVersion;
        _uncommittedEvents.Clear();
    }

    /// <summary>
    ///     Creates a snapshot of the current state.
    /// </summary>
    protected async Task SaveSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CreateSnapshot();
        await _eventStore.SaveSnapshotAsync(ActorId, snapshot, _version, cancellationToken);
    }

    /// <summary>
    ///     When overridden in a derived class, applies an event to the actor's state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    protected abstract void Apply(DomainEvent @event);

    /// <summary>
    ///     When overridden in a derived class, creates a snapshot of the current state.
    /// </summary>
    /// <returns>The snapshot object.</returns>
    protected abstract object CreateSnapshot();

    /// <summary>
    ///     When overridden in a derived class, restores state from a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore from.</param>
    protected abstract void ApplySnapshot(object snapshot);
}
