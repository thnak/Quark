using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

public sealed class PeerConnectionManagerTests
{
    private static readonly SiloAddress Self = SiloAddress.Loopback(11111);
    private static readonly SiloAddress Peer = SiloAddress.Loopback(11112);

    private static PeerConnectionManager Build(
        IMembershipTable table,
        ISiloRouter router,
        DefaultClusterMembershipSnapshot snapshot)
    {
        var options = Options.Create(new SiloRuntimeOptions { SiloAddress = Self });
        return new PeerConnectionManager(
            table, router, snapshot, options, new StubTransport(),
            NullLogger<PeerConnectionManager>.Instance);
    }

    [Fact]
    public async Task StartAsync_RegistersPeersFromMembershipTable()
    {
        var table = new InMemoryMembershipTable();
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Self, SiloName = "self", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });

        var router = new NetworkedSiloRouter();
        var snapshot = new DefaultClusterMembershipSnapshot(Self);
        var manager = Build(table, router, snapshot);

        await manager.StartAsync(CancellationToken.None);

        Assert.True(router.TryGetInvoker(Peer, out _));
        // Self is NOT registered by PeerConnectionManager (SiloHostedService owns that)
        Assert.False(router.TryGetInvoker(Self, out _));

        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_UpdatesSnapshotWithAllActiveSilos()
    {
        var table = new InMemoryMembershipTable();
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Self, SiloName = "self", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });

        var router = new NetworkedSiloRouter();
        var snapshot = new DefaultClusterMembershipSnapshot(Self);
        var manager = Build(table, router, snapshot);

        await manager.StartAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.ActiveSilos.Count);
        Assert.Contains(Self, snapshot.ActiveSilos);
        Assert.Contains(Peer, snapshot.ActiveSilos);

        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DeadPeer_IsUnregisteredFromRouter()
    {
        var table = new InMemoryMembershipTable();
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Self, SiloName = "self", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });

        var router = new NetworkedSiloRouter();
        var snapshot = new DefaultClusterMembershipSnapshot(Self);
        var manager = Build(table, router, snapshot);

        await manager.StartAsync(CancellationToken.None);
        Assert.True(router.TryGetInvoker(Peer, out _));

        await table.UpdateRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Dead, IAmAlive = DateTime.UtcNow
        });

        await manager.RefreshAsync(CancellationToken.None);

        Assert.False(router.TryGetInvoker(Peer, out _));

        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DeadPeer_RemovedFromSnapshot()
    {
        var table = new InMemoryMembershipTable();
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Self, SiloName = "self", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });

        var router = new NetworkedSiloRouter();
        var snapshot = new DefaultClusterMembershipSnapshot(Self);
        var manager = Build(table, router, snapshot);

        await manager.StartAsync(CancellationToken.None);
        Assert.Equal(2, snapshot.ActiveSilos.Count);

        await table.UpdateRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Dead, IAmAlive = DateTime.UtcNow
        });
        await manager.RefreshAsync(CancellationToken.None);

        Assert.Single(snapshot.ActiveSilos);
        Assert.DoesNotContain(Peer, snapshot.ActiveSilos);

        await manager.StopAsync(CancellationToken.None);
    }

    private sealed class StubTransport : ITransport
    {
        public string Name => "stub";
        public ITransportListener CreateListener(System.Net.EndPoint endPoint)
            => throw new NotSupportedException();
        public Task<ITransportConnection> ConnectAsync(System.Net.EndPoint endPoint, CancellationToken ct = default)
            => throw new NotSupportedException("Stub transport — no real TCP in this test.");
    }
}
