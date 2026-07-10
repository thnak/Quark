using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class JournaledGrainSnapshotTests
{
    // ---- Write-path tests (Task 3) ----

    [Fact]
    public async Task ConfirmEvents_WritesSnapshot_WhenIntervalReached()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 3, NewId());

        g.Bump(); g.Bump(); g.Bump();
        await g.SaveAsync();                       // ConfirmedVersion 0 -> 3

        Assert.Single(snap.Writes);
        Assert.Equal(3, snap.Writes[0].Version);
    }

    [Fact]
    public async Task ConfirmEvents_DoesNotSnapshot_BelowInterval()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 3, NewId());

        g.Bump(); g.Bump();
        await g.SaveAsync();                       // ConfirmedVersion 0 -> 2

        Assert.Empty(snap.Writes);
    }

    [Fact]
    public async Task SnapshotInterval_Zero_DisablesAutoSnapshot()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 0, NewId());

        for (int i = 0; i < 5; i++) g.Bump();
        await g.SaveAsync();

        Assert.Empty(snap.Writes);
    }

    [Fact]
    public async Task WriteSnapshotAsync_Manual_WritesAtCurrentVersion()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 0, NewId());

        g.Bump(); g.Bump();
        await g.SaveAsync();                       // version 2, no auto snapshot (interval 0)
        await g.SnapshotNowAsync();

        Assert.Single(snap.Writes);
        Assert.Equal(2, snap.Writes[0].Version);
    }

    [Fact]
    public async Task WriteSnapshotAsync_NoStore_IsNoOp()
    {
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snapshotStore: null, interval: 3, NewId());
        g.Bump();
        await g.SaveAsync();
        await g.SnapshotNowAsync();                // must not throw
        Assert.Equal(1, g.Version);
    }

    // ---- Shared helpers ----

    private static GrainId NewId() => new(new GrainType("CounterGrain"), Guid.NewGuid().ToString("N"));

    private static async Task<CounterGrain> ActivateAsync(
        ILogStorage? log, ISnapshotStore? snapshotStore, int interval, GrainId id)
    {
        var holder = new StateHolder<JournaledGrainState<CounterState, CounterEvent>>();
        var memory = new ActivationMemoryAccessor<JournaledGrainState<CounterState, CounterEvent>>(holder);
        var grain = new CounterGrain(memory, new FixedCallContext(id), log, snapshotStore, interval);
        await grain.OnActivateAsync(CancellationToken.None);
        return grain;
    }

    public sealed class CounterState { public int Count { get; set; } }

    public abstract record CounterEvent;
    public sealed record Bumped : CounterEvent;

    public sealed class CounterGrain : JournaledGrain<CounterState, CounterEvent>
    {
        private readonly int _interval;

        public CounterGrain(
            IActivationMemory<JournaledGrainState<CounterState, CounterEvent>> memory,
            ICallContext ctx,
            ILogStorage? log,
            ISnapshotStore? snapshotStore,
            int interval)
            : base(memory, ctx, log, snapshotStore)
            => _interval = interval;

        protected override int SnapshotInterval => _interval;

        public new CounterState State => base.State;
        public new int Version => base.Version;

        public void Bump() => RaiseEvent(new Bumped());
        public Task SaveAsync() => ConfirmEventsAsync();
        public Task SnapshotNowAsync() => WriteSnapshotAsync();

        protected override void TransitionState(CounterState state, CounterEvent @event) => state.Count++;
    }

    private sealed class FixedCallContext(GrainId grainId) : ICallContext
    {
        public GrainId GrainId => grainId;
    }

    private sealed class FakeSnapshotStore : ISnapshotStore
    {
        private readonly Dictionary<GrainId, object> _snaps = [];
        public List<(GrainId Id, int Version)> Writes { get; } = [];
        public Func<GrainId, Exception?>? ReadThrows { get; set; }

        public void Seed<TState>(GrainId id, SnapshotEnvelope<TState> snap) where TState : class
            => _snaps[id] = snap;

        public Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
            GrainId grainId, CancellationToken ct = default) where TState : class
        {
            if (ReadThrows?.Invoke(grainId) is { } ex) throw ex;
            return Task.FromResult(_snaps.TryGetValue(grainId, out object? s)
                ? (SnapshotEnvelope<TState>?)s
                : null);
        }

        public Task WriteSnapshotAsync<TState>(
            GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
            where TState : class
        {
            Writes.Add((grainId, snapshot.Version));
            _snaps[grainId] = snapshot;
            return Task.CompletedTask;
        }

        public Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default)
        {
            _snaps.Remove(grainId);
            return Task.CompletedTask;
        }
    }
}
