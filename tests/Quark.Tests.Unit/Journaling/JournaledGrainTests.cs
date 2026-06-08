using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class JournaledGrainTests
{
    private static async Task<TodoGrain> ActivateAsync(
        InMemoryLogStorage? storage = null, GrainId? id = null)
    {
        var grainId = id ?? new GrainId(new GrainType("TodoGrain"), "1");
        var holder = new StateHolder<JournaledGrainState<TodoState, TodoEvent>>();
        var ctx = new FixedCallContext(grainId);
        var memory = new ActivationMemoryAccessor<JournaledGrainState<TodoState, TodoEvent>>(holder);
        var grain = new TodoGrain(memory, ctx, storage);
        await grain.OnActivateAsync(CancellationToken.None);
        return grain;
    }

    [Fact]
    public async Task RaiseEvent_UpdatesInMemoryState()
    {
        TodoGrain grain = await ActivateAsync();
        grain.Add("buy milk");
        Assert.Contains("buy milk", grain.State.Items);
    }

    [Fact]
    public async Task ConfirmEventsAsync_PersistsToLog()
    {
        TodoGrain grain = await ActivateAsync(new InMemoryLogStorage());
        grain.Add("task A");
        await grain.SaveAsync();
        Assert.Equal(1, grain.Version);
    }

    [Fact]
    public async Task RetrieveConfirmedEvents_ReturnsHistory()
    {
        TodoGrain grain = await ActivateAsync(new InMemoryLogStorage());
        grain.Add("A");
        grain.Add("B");
        await grain.SaveAsync();

        IReadOnlyList<TodoEvent> history = await grain.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("A", ((ItemAdded)history[0]).Item);
        Assert.Equal("B", ((ItemAdded)history[1]).Item);
    }

    [Fact]
    public async Task OnActivateAsync_RebuildsStateFromLog()
    {
        var storage = new InMemoryLogStorage();
        var grainId = new GrainId(new GrainType("TodoGrain"), "reload-test");

        TodoGrain grain1 = await ActivateAsync(storage, grainId);
        grain1.Add("first");
        grain1.Add("second");
        await grain1.SaveAsync();

        TodoGrain grain2 = await ActivateAsync(storage, grainId);
        Assert.Equal(2, grain2.Version);
        Assert.Contains("first", grain2.State.Items);
        Assert.Contains("second", grain2.State.Items);
    }

    [Fact]
    public async Task StagedEvents_NotVisibleInRetrieveConfirmedEvents()
    {
        TodoGrain grain = await ActivateAsync(new InMemoryLogStorage());
        grain.Add("staged");
        Assert.Equal(0, grain.Version);
        IReadOnlyList<TodoEvent> history = await grain.GetHistoryAsync();
        Assert.Empty(history);
    }

    [Fact]
    public async Task VersionConflict_ThrowsOnConcurrentAppend()
    {
        var storage = new InMemoryLogStorage();
        var grainId = new GrainId(new GrainType("TodoGrain"), "conflict-test");

        TodoGrain grain1 = await ActivateAsync(storage, grainId);
        TodoGrain grain2 = await ActivateAsync(storage, grainId);

        grain1.Add("from grain1");
        await grain1.SaveAsync();

        grain2.Add("from grain2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain2.SaveAsync());
    }

    // ---- Test grain helpers ----

    public sealed class TodoState
    {
        public List<string> Items { get; } = [];
    }

    public abstract record TodoEvent;
    public sealed record ItemAdded(string Item) : TodoEvent;
    public sealed record ItemRemoved(string Item) : TodoEvent;

    public sealed class TodoGrain : JournaledGrain<TodoState, TodoEvent>
    {
        public TodoGrain(
            IActivationMemory<JournaledGrainState<TodoState, TodoEvent>> memory,
            ICallContext ctx,
            ILogStorage? storage = null)
            : base(memory, ctx, storage) { }

        public new TodoState State => base.State;
        public new int Version => base.Version;

        public void Add(string item) => RaiseEvent(new ItemAdded(item));
        public void Remove(string item) => RaiseEvent(new ItemRemoved(item));
        public Task SaveAsync() => ConfirmEventsAsync();
        public Task<IReadOnlyList<TodoEvent>> GetHistoryAsync() => RetrieveConfirmedEvents(0, Version);

        protected override void TransitionState(TodoState state, TodoEvent @event)
        {
            switch (@event)
            {
                case ItemAdded added: state.Items.Add(added.Item); break;
                case ItemRemoved removed: state.Items.Remove(removed.Item); break;
            }
        }
    }

    private sealed class FixedCallContext(GrainId grainId) : ICallContext
    {
        public GrainId GrainId => grainId;
    }
}
