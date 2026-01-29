using System.Collections.Concurrent;
using System.Text.Json;

namespace Quark.EventSourcing;

/// <summary>
///     In-memory implementation of event store for testing and development.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<DomainEvent>> _events = new();
    private readonly ConcurrentDictionary<string, (object Snapshot, long Version)> _snapshots = new();
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryEventStore"/> class.
    /// </summary>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public InMemoryEventStore(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public Task<long> AppendEventsAsync(
        string actorId,
        IReadOnlyList<DomainEvent> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
            return Task.FromResult(0L);

        var eventList = _events.GetOrAdd(actorId, _ => new List<DomainEvent>());

        lock (eventList)
        {
            var currentVersion = eventList.Count > 0 ? eventList[^1].SequenceNumber : 0L;

            if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
            {
                throw new EventStoreConcurrencyException(expectedVersion.Value, currentVersion);
            }

            var nextSequence = currentVersion + 1;
            foreach (var @event in events)
            {
                @event.SequenceNumber = nextSequence++;
                @event.ActorId = actorId;
                eventList.Add(@event);
            }

            return Task.FromResult(nextSequence - 1);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DomainEvent>> ReadEventsAsync(
        string actorId,
        long fromVersion = 0,
        CancellationToken cancellationToken = default)
    {
        if (!_events.TryGetValue(actorId, out var eventList))
        {
            return Task.FromResult<IReadOnlyList<DomainEvent>>(Array.Empty<DomainEvent>());
        }

        lock (eventList)
        {
            var filtered = eventList.Where(e => e.SequenceNumber >= fromVersion).ToList();
            return Task.FromResult<IReadOnlyList<DomainEvent>>(filtered);
        }
    }

    /// <inheritdoc />
    public Task<long> GetCurrentVersionAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (!_events.TryGetValue(actorId, out var eventList))
        {
            return Task.FromResult(0L);
        }

        lock (eventList)
        {
            return Task.FromResult(eventList.Count > 0 ? eventList[^1].SequenceNumber : 0L);
        }
    }

    /// <inheritdoc />
    public Task SaveSnapshotAsync(
        string actorId,
        object snapshot,
        long version,
        CancellationToken cancellationToken = default)
    {
        _snapshots[actorId] = (snapshot, version);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<(object Snapshot, long Version)?> LoadSnapshotAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshots.TryGetValue(actorId, out var snapshot))
        {
            return Task.FromResult<(object, long)?>(snapshot);
        }
        return Task.FromResult<(object, long)?>(null);
    }
}
