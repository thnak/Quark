using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Serialization;

public sealed class CopyContextTests
{
    [Fact]
    public void TryGetCopy_EmptyContext_ReturnsNull()
    {
        var ctx = new CopyContext();
        string? result = ctx.TryGetCopy<string>(new object());
        Assert.Null(result);
    }

    [Fact]
    public void RecordAndRetrieve_RoundTrips()
    {
        var ctx = new CopyContext();
        var original = new object();
        var copy = new object();
        ctx.RecordCopy(original, copy);
        Assert.Same(copy, ctx.TryGetCopy<object>(original));
    }

    [Fact]
    public void SharedReference_ReturnsSameCopyBothTimes()
    {
        var ctx = new CopyContext();
        var shared = new object();
        var copy = new object();
        ctx.RecordCopy(shared, copy);
        Assert.Same(copy, ctx.TryGetCopy<object>(shared));
        Assert.Same(copy, ctx.TryGetCopy<object>(shared));
    }

    [Fact]
    public void Reset_ClearsAllRecords()
    {
        var ctx = new CopyContext();
        var original = new object();
        ctx.RecordCopy(original, new object());
        ctx.Reset();
        Assert.Null(ctx.TryGetCopy<object>(original));
    }

    [Fact]
    public void Reset_OnEmptyContext_DoesNotThrow()
    {
        var ctx = new CopyContext();
        ctx.Reset();
    }

    [Fact]
    public void CyclicGraph_BrokenByRecordCopyBeforeRecursing()
    {
        // Simulate a pair of nodes that reference each other: A.Next = B, B.Next = A.
        // A correct generated copier records the copy of A before recursing into B, so that
        // when copying B.Next it finds the already-recorded copy of A instead of recursing again.
        var ctx = new CopyContext();
        var nodeA = new CycleNode { Id = 1 };
        var nodeB = new CycleNode { Id = 2 };
        nodeA.Next = nodeB;
        nodeB.Next = nodeA;

        var copyA = new CycleNode { Id = 1 };
        var copyB = new CycleNode { Id = 2 };

        ctx.RecordCopy(nodeA, copyA);
        ctx.RecordCopy(nodeB, copyB);

        copyA.Next = ctx.TryGetCopy<CycleNode>(nodeA.Next!)!;
        copyB.Next = ctx.TryGetCopy<CycleNode>(nodeB.Next!)!;

        Assert.Same(copyB, copyA.Next);
        Assert.Same(copyA, copyB.Next);
        Assert.NotSame(nodeA, copyA);
        Assert.NotSame(nodeB, copyB);
    }

    private sealed class CycleNode
    {
        public int Id;
        public CycleNode? Next;
    }
}
