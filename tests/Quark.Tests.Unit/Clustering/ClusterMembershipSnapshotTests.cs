using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime.Clustering;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

public sealed class ClusterMembershipSnapshotTests
{
    [Fact]
    public void DefaultSnapshot_InitiallyContainsOnlySelf()
    {
        var self = SiloAddress.Loopback(11111);
        var snapshot = new DefaultClusterMembershipSnapshot(self);

        Assert.Single(snapshot.ActiveSilos);
        Assert.Contains(self, snapshot.ActiveSilos);
    }

    [Fact]
    public void DefaultSnapshot_Update_ReplacesActiveSilos()
    {
        var self = SiloAddress.Loopback(11111);
        var peer = SiloAddress.Loopback(11112);
        var snapshot = new DefaultClusterMembershipSnapshot(self);

        snapshot.Update([self, peer]);

        Assert.Equal(2, snapshot.ActiveSilos.Count);
        Assert.Contains(self, snapshot.ActiveSilos);
        Assert.Contains(peer, snapshot.ActiveSilos);
    }

    [Fact]
    public void DefaultSnapshot_Implements_IClusterMembershipSnapshot()
    {
        var self = SiloAddress.Loopback(11111);
        IClusterMembershipSnapshot snapshot = new DefaultClusterMembershipSnapshot(self);

        IReadOnlyList<SiloAddress> silos = snapshot.ActiveSilos;

        Assert.NotNull(silos);
        Assert.Single(silos);
    }

    [Fact]
    public void DefaultSnapshot_Update_IsVisibleOnNextRead()
    {
        var self = SiloAddress.Loopback(11111);
        var peer = SiloAddress.Loopback(11112);
        var snapshot = new DefaultClusterMembershipSnapshot(self);

        snapshot.Update([self, peer]);
        IReadOnlyList<SiloAddress> read = snapshot.ActiveSilos;

        Assert.Equal(2, read.Count);
    }
}
