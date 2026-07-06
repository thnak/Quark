using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class InMemoryLogStorageTests
{
    private static GrainId G(string key) => new(new GrainType("Test"), key);

    private static Task AppendAsync(InMemoryLogStorage storage, GrainId id, params object[] events)
    {
        // Build entries starting from version 0 (or whatever the current count is).
        // Caller passes raw events; we wrap them in LogEntry with sequential versions.
        // To correctly handle multi-call scenarios we must read the current version first.
        // For simplicity in these tests, call AppendEntriesAsync with the known expected version.
        // Each test controls its own grain id so versions start at 0.
        var list = new List<LogEntry>(events.Length);
        for (int i = 0; i < events.Length; i++)
            list.Add(new LogEntry(i, events[i]));
        return storage.AppendEntriesAsync(id, 0, list);
    }

    private static GrainId Unique() => G(Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadEntriesAsync_EmptyLog_ReturnsEmpty()
    {
        var storage = new InMemoryLogStorage();
        var result = await storage.ReadEntriesAsync(Unique(), 0, int.MaxValue);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEntriesAsync_UnknownGrain_ReturnsEmpty()
    {
        var storage = new InMemoryLogStorage();
        var result = await storage.ReadEntriesAsync(Unique(), 0, 5);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEntriesAsync_FullRange_ReturnsAllEntries()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a", "b", "c");

        var result = await storage.ReadEntriesAsync(id, 0, int.MaxValue);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].Version);
        Assert.Equal(1, result[1].Version);
        Assert.Equal(2, result[2].Version);
    }

    [Fact]
    public async Task ReadEntriesAsync_PartialRange_ReturnsSlice()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a", "b", "c", "d", "e");

        var result = await storage.ReadEntriesAsync(id, 1, 4);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Version);
        Assert.Equal(2, result[1].Version);
        Assert.Equal(3, result[2].Version);
        Assert.Equal("b", result[0].Event);
        Assert.Equal("d", result[2].Event);
    }

    [Fact]
    public async Task ReadEntriesAsync_ZeroWidthRange_ReturnsEmpty()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a", "b");

        var result = await storage.ReadEntriesAsync(id, 1, 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEntriesAsync_FromVersionEqualsLogCount_ReturnsEmpty()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a", "b");

        var result = await storage.ReadEntriesAsync(id, 2, 10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEntriesAsync_FromVersionBeyondLogCount_ReturnsEmpty()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a");

        var result = await storage.ReadEntriesAsync(id, 99, 200);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEntriesAsync_ToVersionBeyondLogCount_ClampsToEnd()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "a", "b", "c");

        var result = await storage.ReadEntriesAsync(id, 1, 999);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Version);
        Assert.Equal(2, result[1].Version);
    }

    [Fact]
    public async Task ReadEntriesAsync_FromVersionZeroToVersionOne_ReturnsSingleEntry()
    {
        var storage = new InMemoryLogStorage();
        var id = Unique();
        await AppendAsync(storage, id, "only");

        var result = await storage.ReadEntriesAsync(id, 0, 1);

        Assert.Single(result);
        Assert.Equal("only", result[0].Event);
    }

    [Fact]
    public async Task ReadEntriesAsync_MultipleGrains_StoreIsIsolated()
    {
        var storage = new InMemoryLogStorage();
        var id1 = Unique();
        var id2 = Unique();
        await AppendAsync(storage, id1, "x");
        await AppendAsync(storage, id2, "y", "z");

        var r1 = await storage.ReadEntriesAsync(id1, 0, int.MaxValue);
        var r2 = await storage.ReadEntriesAsync(id2, 0, int.MaxValue);

        Assert.Single(r1);
        Assert.Equal("x", r1[0].Event);
        Assert.Equal(2, r2.Count);
    }
}
