using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class InMemorySnapshotStoreTests
{
    // Snapshotted state needs a deep copier. Generators don't run in test projects, so hand-write one.
    public sealed class Bag
    {
        public int N { get; set; }
        public List<string> Items { get; set; } = [];
    }

    private sealed class BagCopier : IDeepCopier<Bag>
    {
        public Bag DeepCopy(Bag original, CopyContext context) =>
            new() { N = original.N, Items = [.. original.Items] };
    }

    private static (InMemorySnapshotStore Store, GrainId Id) NewStore()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddSingleton<IDeepCopier<Bag>>(new BagCopier());
        var sp = services.BuildServiceProvider();
        var store = new InMemorySnapshotStore(sp.GetRequiredService<ICopierProvider>());
        return (store, new GrainId(new GrainType("G"), "k"));
    }

    [Fact]
    public async Task ReadSnapshotAsync_ReturnsNull_WhenMissing()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        Assert.Null(await store.ReadSnapshotAsync<Bag>(id));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsVersionAndState()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(5, new Bag { N = 9, Items = ["a"] }));

        SnapshotEnvelope<Bag>? read = await store.ReadSnapshotAsync<Bag>(id);
        Assert.NotNull(read);
        Assert.Equal(5, read!.Version);
        Assert.Equal(9, read.State.N);
        Assert.Equal(new[] { "a" }, read.State.Items);
    }

    [Fact]
    public async Task Write_IsolatesFromLaterMutationOfOriginal()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        var live = new Bag { N = 1, Items = ["x"] };
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, live));

        live.N = 99;            // mutate the live state after the snapshot was taken
        live.Items.Add("y");

        SnapshotEnvelope<Bag>? read = await store.ReadSnapshotAsync<Bag>(id);
        Assert.Equal(1, read!.State.N);
        Assert.Equal(new[] { "x" }, read.State.Items);
    }

    [Fact]
    public async Task Read_IsolatesStoredCopyFromCallerMutation()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, new Bag { N = 1, Items = ["x"] }));

        SnapshotEnvelope<Bag>? first = await store.ReadSnapshotAsync<Bag>(id);
        first!.State.N = 42;               // caller mutates the returned copy
        first.State.Items.Add("z");

        SnapshotEnvelope<Bag>? second = await store.ReadSnapshotAsync<Bag>(id);
        Assert.Equal(1, second!.State.N);
        Assert.Equal(new[] { "x" }, second.State.Items);
    }

    [Fact]
    public async Task ClearSnapshotAsync_RemovesSnapshot()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, new Bag { N = 1 }));
        await store.ClearSnapshotAsync(id);
        Assert.Null(await store.ReadSnapshotAsync<Bag>(id));
    }
}
