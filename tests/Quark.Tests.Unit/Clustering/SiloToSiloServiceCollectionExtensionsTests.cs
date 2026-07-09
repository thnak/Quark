using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

/// <summary>
///     Verifies <c>AddSiloToSiloTransport()</c> resolves a complete, working DI graph —
///     the exact registration path <see cref="SiloToSiloFaultTests" /> and
///     <see cref="PeerConnectionManagerTests" /> bypass by constructing types manually (issue #156).
/// </summary>
public sealed class SiloToSiloServiceCollectionExtensionsTests
{
    private static readonly SiloAddress Self = SiloAddress.Loopback(13001);

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuarkRuntime();
        services.Configure<SiloRuntimeOptions>(o => o.SiloAddress = Self);
        services.AddSingleton<IMembershipTable, InMemoryMembershipTable>();
        services.AddSingleton<ITransport, StubTransport>();
        services.AddSiloToSiloTransport();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvesSiloRouter_AsNetworkedSiloRouterSingleton()
    {
        using ServiceProvider sp = BuildProvider();

        ISiloRouter router = sp.GetRequiredService<ISiloRouter>();
        Assert.Same(sp.GetRequiredService<NetworkedSiloRouter>(), router);
    }

    [Fact]
    public void ResolvesClusterMembershipSnapshot_AsDefaultClusterMembershipSnapshotSingleton()
    {
        using ServiceProvider sp = BuildProvider();

        IClusterMembershipSnapshot snapshot = sp.GetRequiredService<IClusterMembershipSnapshot>();
        Assert.Same(sp.GetRequiredService<DefaultClusterMembershipSnapshot>(), snapshot);
    }

    [Fact]
    public async Task ResolvesSiloTerminalInvoker_WithoutMissingDependencies()
    {
        await using ServiceProvider sp = BuildProvider();

        IGrainCallInvoker invoker = sp.GetRequiredKeyedService<IGrainCallInvoker>("silo-terminal");
        Assert.IsType<LocalGrainCallInvoker>(invoker);
    }

    [Fact]
    public async Task PeerConnectionManager_StartsAndStopsAgainstMembershipTable()
    {
        await using ServiceProvider sp = BuildProvider();

        IMembershipTable table = sp.GetRequiredService<IMembershipTable>();
        await table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = Self, SiloName = "self", Status = SiloStatus.Active, IAmAlive = DateTime.UtcNow
        });

        PeerConnectionManager manager = Assert.Single(sp.GetServices<IHostedService>().OfType<PeerConnectionManager>());

        await manager.StartAsync(CancellationToken.None);
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
