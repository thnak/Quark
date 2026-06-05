using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class JournaledGrainTests
{
    private static async Task<(TodoGrain grain, GrainContext ctx)> ActivateAsync(
        InMemoryLogStorage? storage = null, GrainId? id = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var grain = new TodoGrain(storage ?? new InMemoryLogStorage());
        var grainId = id ?? new GrainId(new GrainType("TodoGrain"), "1");
        var ctx = new GrainContext(grainId, new NullGrainFactory(), provider);
        await ctx.ActivateAsync(grain);
        return (grain, ctx);
    }

    [Fact]
    public async Task RaiseEvent_UpdatesInMemoryState()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("buy milk");
        Assert.Contains("buy milk", grain.State.Items);
    }

    [Fact]
    public async Task ConfirmEventsAsync_PersistsToLog()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("task A");
        await grain.SaveAsync();
        Assert.Equal(1, grain.Version);
    }

    [Fact]
    public async Task RetrieveConfirmedEvents_ReturnsHistory()
    {
        var (grain, _) = await ActivateAsync();
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

        // First activation: write some events and confirm them
        var (grain1, _) = await ActivateAsync(storage, grainId);
        grain1.Add("first");
        grain1.Add("second");
        await grain1.SaveAsync();

        // Second activation on the same ID and storage: should reload state
        var (grain2, _) = await ActivateAsync(storage, grainId);
        Assert.Equal(2, grain2.Version);
        Assert.Contains("first", grain2.State.Items);
        Assert.Contains("second", grain2.State.Items);
    }

    [Fact]
    public async Task StagedEvents_NotVisibleInRetrieveConfirmedEvents()
    {
        var (grain, _) = await ActivateAsync();
        grain.Add("staged");
        // Not confirmed yet — Version is still 0
        Assert.Equal(0, grain.Version);
        IReadOnlyList<TodoEvent> history = await grain.GetHistoryAsync();
        Assert.Empty(history);
    }

    [Fact]
    public async Task VersionConflict_ThrowsOnConcurrentAppend()
    {
        var storage = new InMemoryLogStorage();
        var grainId = new GrainId(new GrainType("TodoGrain"), "conflict-test");

        var (grain1, _) = await ActivateAsync(storage, grainId);
        var (grain2, _) = await ActivateAsync(storage, grainId);

        grain1.Add("from grain1");
        await grain1.SaveAsync();   // succeeds — appends at version 0

        grain2.Add("from grain2");
        // grain2 also believes it is at version 0 → conflict
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain2.SaveAsync());
    }

    // ---- Infrastructure fakes ----

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey
            => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey
            => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey
            => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey
            => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey
            => throw new NotImplementedException();
        public IGrain GetGrain(Type t, string key) => throw new NotImplementedException();
        public IGrain GetGrain(Type t, Guid key) => throw new NotImplementedException();
        public IGrain GetGrain(Type t, long key) => throw new NotImplementedException();
    }

    // ---- Test grain types ----

    public sealed class TodoState
    {
        public List<string> Items { get; } = [];
    }

    public abstract record TodoEvent;
    public sealed record ItemAdded(string Item) : TodoEvent;
    public sealed record ItemRemoved(string Item) : TodoEvent;

    public sealed class TodoGrain : JournaledGrain<TodoState, TodoEvent>
    {
        public TodoGrain(InMemoryLogStorage storage) => InjectLogStorage(storage);

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
                case ItemAdded added:   state.Items.Add(added.Item); break;
                case ItemRemoved removed: state.Items.Remove(removed.Item); break;
            }
        }
    }
}
