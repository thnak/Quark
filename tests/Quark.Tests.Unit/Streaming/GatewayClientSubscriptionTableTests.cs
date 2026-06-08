using Quark.Runtime;
using Quark.Streaming.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class GatewayClientSubscriptionTableTests
{
    [Fact]
    public void Add_And_Remove_Works()
    {
        var table = new GatewayClientSubscriptionTable();
        var subId = Guid.NewGuid();
        var sub = MakeSub(subId);

        table.Add(sub);
        table.Remove(subId);
        table.Remove(subId); // idempotent remove should not throw
    }

    [Fact]
    public void RemoveAll_RemovesMultiple()
    {
        var table = new GatewayClientSubscriptionTable();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
            table.Add(MakeSub(id));

        table.RemoveAll(ids);

        // Re-add should work (verifies they were removed)
        foreach (var id in ids)
            table.Add(MakeSub(id));
    }

    private static GatewayClientSubscription MakeSub(Guid id) =>
        new(id, StreamId.Create("ns", "key"), null!, (_, _) => Task.CompletedTask);
}
