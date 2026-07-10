using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Abstract base class for event-sourced grain behaviors.
///     Subclasses implement <see cref="TransitionState" /> to apply events to state.
///     Cross-call state lives in <see cref="IActivationMemory{T}" /> so it is owned
///     by the shell and survives across method calls on the same activation.
/// </summary>
public abstract class JournaledGrain<TState, TEvent> : IGrainBehavior, IActivationLifecycle
    where TState : class, new()
{
    private readonly IActivationMemory<JournaledGrainState<TState, TEvent>> _memory;
    private readonly ICallContext _ctx;
    private ILogStorage? _logStorage;
    private ISnapshotStore? _snapshotStore;

    protected JournaledGrain(
        IActivationMemory<JournaledGrainState<TState, TEvent>> memory,
        ICallContext ctx,
        ILogStorage? logStorage = null,
        ISnapshotStore? snapshotStore = null)
    {
        _memory = memory;
        _ctx = ctx;
        _logStorage = logStorage;
        _snapshotStore = snapshotStore;
    }

    /// <summary>The grain identity for this call.</summary>
    protected GrainId GrainId => _ctx.GrainId;

    /// <summary>The number of confirmed (persisted) events.</summary>
    protected int Version => _memory.Value.ConfirmedVersion;

    /// <summary>The current in-memory state (includes staged but not-yet-confirmed events).</summary>
    protected TState State => _memory.Value.State;

    /// <summary>
    ///     Number of confirmed events between automatic snapshots. Override per grain type.
    ///     <c>0</c> disables automatic snapshotting for this grain type. Default: 100.
    ///     Automatic snapshots require a registered <see cref="ISnapshotStore" />.
    /// </summary>
    protected virtual int SnapshotInterval => 100;

    /// <inheritdoc />
    public async Task OnActivateAsync(CancellationToken ct)
    {
        if (_logStorage is not null)
            await ReloadFromLogAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    ///     Applies an event to <see cref="State" /> in memory.
    ///     Override this to describe how state transitions. Implement as a pure function.
    /// </summary>
    protected abstract void TransitionState(TState state, TEvent @event);

    /// <summary>Stages an event: applies it in memory immediately. Call <see cref="ConfirmEventsAsync" /> to persist.</summary>
    protected void RaiseEvent(TEvent @event)
    {
        _memory.Value.StagedEvents.Add(@event);
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
        JournaledGrainState<TState, TEvent> st = _memory.Value;
        if (st.StagedEvents.Count == 0) return;
        if (_logStorage is null) throw new InvalidOperationException("No ILogStorage injected or registered.");

        var entries = st.StagedEvents
            .Select((e, i) => new LogEntry(st.ConfirmedVersion + i, e!))
            .ToList();

        await _logStorage.AppendEntriesAsync(GrainId, st.ConfirmedVersion, entries, cancellationToken)
            .ConfigureAwait(false);
        st.ConfirmedVersion += st.StagedEvents.Count;
        st.StagedEvents.Clear();

        if (_snapshotStore is not null && SnapshotInterval > 0 &&
            st.ConfirmedVersion - st.LastSnapshotVersion >= SnapshotInterval)
        {
            await WriteSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Writes a snapshot of the current confirmed state to the registered
    ///     <see cref="ISnapshotStore" />. No-op when no snapshot store is registered.
    /// </summary>
    protected Task WriteSnapshotAsync(CancellationToken cancellationToken = default) =>
        _snapshotStore is null ? Task.CompletedTask : WriteSnapshotCoreAsync(cancellationToken);

    private async Task WriteSnapshotCoreAsync(CancellationToken ct)
    {
        JournaledGrainState<TState, TEvent> st = _memory.Value;
        await _snapshotStore!
            .WriteSnapshotAsync(GrainId, new SnapshotEnvelope<TState>(st.ConfirmedVersion, st.State), ct)
            .ConfigureAwait(false);
        st.LastSnapshotVersion = st.ConfirmedVersion;
    }

    /// <summary>Retrieves confirmed events in the range [<paramref name="fromVersion"/>, <paramref name="toVersion"/>).</summary>
    protected async Task<IReadOnlyList<TEvent>> RetrieveConfirmedEvents(
        int fromVersion, int toVersion, CancellationToken cancellationToken = default)
    {
        if (_logStorage is null) return [];
        IReadOnlyList<LogEntry> entries =
            await _logStorage.ReadEntriesAsync(GrainId, fromVersion, toVersion, cancellationToken)
                .ConfigureAwait(false);
        return entries.Select(e => (TEvent)e.Event).ToList();
    }

    private async Task ReloadFromLogAsync(CancellationToken ct)
    {
        JournaledGrainState<TState, TEvent> st = _memory.Value;
        st.ConfirmedVersion = 0;
        st.LastSnapshotVersion = 0;

        if (_snapshotStore is not null)
        {
            SnapshotEnvelope<TState>? snap =
                await _snapshotStore.ReadSnapshotAsync<TState>(GrainId, ct).ConfigureAwait(false);

            if (snap is not null && snap.Version > 0)
            {
                // Boundary probe: read from snap.Version - 1 so we can confirm the log actually
                // contains >= snap.Version contiguous entries WITHOUT a length API on ILogStorage
                // (AppendEntriesAsync guarantees version == index, so entry[V-1] existing => 0..V-1 exist).
                IReadOnlyList<LogEntry> tail = await _logStorage!
                    .ReadEntriesAsync(GrainId, snap.Version - 1, int.MaxValue, ct).ConfigureAwait(false);

                if (tail.Count == 0 || tail[0].Version != snap.Version - 1)
                    throw new CorruptSnapshotException(GrainId, snap.Version,
                        $"Snapshot version {snap.Version} is ahead of the event log for grain {GrainId}.");

                st.State = snap.State;                 // store returned an isolated copy
                st.ConfirmedVersion = snap.Version;
                st.LastSnapshotVersion = snap.Version;

                for (int i = 1; i < tail.Count; i++)   // skip the boundary entry (already in the snapshot)
                {
                    TransitionState(st.State, (TEvent)tail[i].Event);
                    st.ConfirmedVersion = tail[i].Version + 1;
                }

                return;
            }
        }

        // No usable snapshot: full replay from 0 (original behavior).
        st.State = new TState();
        IReadOnlyList<LogEntry> all =
            await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct).ConfigureAwait(false);
        foreach (LogEntry entry in all)
        {
            TransitionState(st.State, (TEvent)entry.Event);
            st.ConfirmedVersion = entry.Version + 1;
        }
    }
}
