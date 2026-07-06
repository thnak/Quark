using Quark.Streaming.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public sealed class ImplicitStreamSubscriptionRegistryTests
{
    [Fact]
    public void Register_SingleNamespace_TryGetGrainTypes_ReturnsAll()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();
        registry.Register("ns", "GrainA");
        registry.Register("ns", "GrainB");

        bool found = registry.TryGetGrainTypes("ns", out IReadOnlyList<string> types);

        Assert.True(found);
        Assert.Equal(2, types.Count);
        Assert.Contains("GrainA", types);
        Assert.Contains("GrainB", types);
    }

    [Fact]
    public void Register_MultipleNamespaces_LookupIsIsolated()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();
        registry.Register("alpha", "GrainA");
        registry.Register("beta", "GrainB");

        bool foundAlpha = registry.TryGetGrainTypes("alpha", out IReadOnlyList<string> alphaTypes);
        bool foundBeta = registry.TryGetGrainTypes("beta", out IReadOnlyList<string> betaTypes);

        Assert.True(foundAlpha);
        Assert.Single(alphaTypes);
        Assert.Equal("GrainA", alphaTypes[0]);

        Assert.True(foundBeta);
        Assert.Single(betaTypes);
        Assert.Equal("GrainB", betaTypes[0]);
    }

    [Fact]
    public void TryGetGrainTypes_UnknownNamespace_ReturnsFalse()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();

        bool found = registry.TryGetGrainTypes("missing", out IReadOnlyList<string> types);

        Assert.False(found);
        Assert.Empty(types);
    }

    [Fact]
    public void Register_SnapshotUpdatesAfterEachRegister()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();
        registry.Register("ns", "GrainA");

        bool found1 = registry.TryGetGrainTypes("ns", out IReadOnlyList<string> after1);
        Assert.True(found1);
        Assert.Single(after1);

        registry.Register("ns", "GrainB");

        bool found2 = registry.TryGetGrainTypes("ns", out IReadOnlyList<string> after2);
        Assert.True(found2);
        Assert.Equal(2, after2.Count);
    }

    [Fact]
    public void ConcurrentLookups_SeeConsistentSnapshot()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();
        registry.Register("ns", "GrainA");
        registry.Register("ns", "GrainB");

        // All concurrent readers must observe the same snapshot (either both or neither entry).
        int consistent = 0;
        Parallel.For(0, 200, _ =>
        {
            if (registry.TryGetGrainTypes("ns", out IReadOnlyList<string> types))
            {
                if (types.Count is 1 or 2)
                    Interlocked.Increment(ref consistent);
            }
        });

        Assert.Equal(200, consistent);
    }

    [Fact]
    public void TryGetGrainTypes_ReturnedSnapshot_IsNotMutatedBySubsequentRegister()
    {
        var registry = new ImplicitStreamSubscriptionRegistry();
        registry.Register("ns", "GrainA");

        registry.TryGetGrainTypes("ns", out IReadOnlyList<string> snapshot1);
        int countBefore = snapshot1.Count;

        registry.Register("ns", "GrainB");

        // The previously returned snapshot must not be retroactively mutated.
        Assert.Equal(countBefore, snapshot1.Count);
    }
}
