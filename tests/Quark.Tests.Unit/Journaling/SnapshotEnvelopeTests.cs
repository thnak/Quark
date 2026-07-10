using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class SnapshotEnvelopeTests
{
    private sealed class State { public int N { get; set; } }

    [Fact]
    public void SnapshotEnvelope_ExposesVersionAndState()
    {
        var s = new State { N = 7 };
        var env = new SnapshotEnvelope<State>(3, s);
        Assert.Equal(3, env.Version);
        Assert.Same(s, env.State);
    }

    [Fact]
    public void CorruptSnapshotException_CarriesGrainIdAndVersion()
    {
        var id = new GrainId(new GrainType("G"), "k");
        var ex = new CorruptSnapshotException(id, 42, "boom");
        Assert.Equal(id, ex.GrainId);
        Assert.Equal(42, ex.SnapshotVersion);
        Assert.Contains("boom", ex.Message);
    }
}
