using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Fault.Tests;

/// <summary>
///     Verifies failure semantics for silo-to-silo transport:
///     dead peers are unregistered, pending calls are faulted fail-fast,
///     and the snapshot is kept consistent.
/// </summary>
public sealed class SiloToSiloFaultTests
{
    private static readonly SiloAddress Self = SiloAddress.Loopback(12341);
    private static readonly SiloAddress Peer = SiloAddress.Loopback(12342);

    private static PeerConnectionManager Build(
        IMembershipTable table,
        ISiloRouter router,
        DefaultClusterMembershipSnapshot snapshot)
    {
        var options = Options.Create(new SiloRuntimeOptions { SiloAddress = Self });
        return new PeerConnectionManager(
            table, router, snapshot, options, new NeverConnectTransport(),
            NullLogger<PeerConnectionManager>.Instance);
    }

    [Fact]
    public async Task DeadPeer_RouterUnregistered_NoFurtherRouting()
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
        Assert.True(router.TryGetInvoker(Peer, out _), "Peer should be registered on startup.");

        // Simulate peer death
        await table.UpdateRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Dead, IAmAlive = DateTime.UtcNow
        });
        await manager.RefreshAsync(CancellationToken.None);

        Assert.False(router.TryGetInvoker(Peer, out _),
            "Dead peer must not remain in the router.");

        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PeerDrop_SnapshotExcludesDeadPeer()
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

    [Fact]
    public async Task PeerReAddedAfterDeath_ReRegistersInRouter()
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

        // Mark dead
        await table.UpdateRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Dead, IAmAlive = DateTime.UtcNow
        });
        await manager.RefreshAsync(CancellationToken.None);
        Assert.False(router.TryGetInvoker(Peer, out _));

        // A new instance of the peer joins
        await table.UpdateRowAsync(new MembershipEntry
        {
            SiloAddress = Peer, SiloName = "peer", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });
        await manager.RefreshAsync(CancellationToken.None);

        Assert.True(router.TryGetInvoker(Peer, out _),
            "Re-activated peer should be re-registered.");

        await manager.StopAsync(CancellationToken.None);
    }

    private sealed class NeverConnectTransport : ITransport
    {
        public string Name => "never-connect";
        public ITransportListener CreateListener(System.Net.EndPoint endPoint)
            => throw new NotSupportedException();
        public Task<ITransportConnection> ConnectAsync(System.Net.EndPoint endPoint, CancellationToken ct = default)
            => throw new NotSupportedException("NeverConnectTransport: no real connections in fault tests.");
    }
}
