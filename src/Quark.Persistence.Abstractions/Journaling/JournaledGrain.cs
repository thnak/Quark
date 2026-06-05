using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Abstract base class for event-sourced grains.
///     Subclasses implement <see cref="TransitionState" /> to apply events to state.
///     Drop-in equivalent of Orleans' <c>JournaledGrain&lt;TState, TEvent&gt;</c>.
/// </summary>
public abstract class JournaledGrain<TState, TEvent> : Grain
    where TState : class, new()
{
    private readonly List<TEvent> _stagedEvents = [];
    private ILogStorage? _logStorage;
    private int _confirmedVersion;

    /// <summary>The number of confirmed (persisted) events.</summary>
    protected int Version => _confirmedVersion;

    /// <summary>The current in-memory state (includes staged but not-yet-confirmed events).</summary>
    protected TState State { get; private set; } = new();

    /// <summary>
    ///     Injects the log storage. Call from a hand-written activator factory or a test constructor.
    ///     If not called, <see cref="OnActivateAsync" /> falls back to <c>IServiceProvider</c>.
    /// </summary>
    protected void InjectLogStorage(ILogStorage logStorage) => _logStorage = logStorage;

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logStorage ??= ServiceProvider.GetService<ILogStorage>();
        if (_logStorage is not null)
            await ReloadFromLogAsync(cancellationToken).ConfigureAwait(false);
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Applies an event to <see cref="State" /> in memory.
    ///     Override this to describe how state transitions. Implement as a pure function.
    /// </summary>
    protected abstract void TransitionState(TState state, TEvent @event);

    /// <summary>Stages an event: applies it in memory immediately. Call <see cref="ConfirmEventsAsync" /> to persist.</summary>
    protected void RaiseEvent(TEvent @event)
    {
        _stagedEvents.Add(@event);
        TransitionState(State, @event);
    }

    /// <summary>Stages multiple events.</summary>
    protected void RaiseEvents(IEnumerable<TEvent> events)
    {
        foreach (TEvent e in events) RaiseEvent(e);
    }

    /// <summary>Persists all staged events to the log storage.</summary>
    protected async Task ConfirmEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_stagedEvents.Count == 0) return;
        if (_logStorage is null) throw new InvalidOperationException("No ILogStorage injected or registered.");

        var entries = _stagedEvents
            .Select((e, i) => new LogEntry(_confirmedVersion + i, e!))
            .ToList();

        await _logStorage.AppendEntriesAsync(GrainId, _confirmedVersion, entries, cancellationToken).ConfigureAwait(false);
        _confirmedVersion += _stagedEvents.Count;
        _stagedEvents.Clear();
    }

    /// <summary>Retrieves confirmed events in the range [<paramref name="fromVersion"/>, <paramref name="toVersion"/>).</summary>
    protected async Task<IReadOnlyList<TEvent>> RetrieveConfirmedEvents(
        int fromVersion, int toVersion, CancellationToken cancellationToken = default)
    {
        if (_logStorage is null) return [];
        IReadOnlyList<LogEntry> entries =
            await _logStorage.ReadEntriesAsync(GrainId, fromVersion, toVersion, cancellationToken).ConfigureAwait(false);
        return entries.Select(e => (TEvent)e.Event).ToList();
    }

    private async Task ReloadFromLogAsync(CancellationToken ct)
    {
        IReadOnlyList<LogEntry> all =
            await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct).ConfigureAwait(false);
        State = new TState();
        foreach (LogEntry entry in all)
        {
            TransitionState(State, (TEvent)entry.Event);
            _confirmedVersion = entry.Version + 1;
        }
    }
}
