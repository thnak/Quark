using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Runtime;
using Quark.Testing.Harness;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

/// <summary>
///     Verifies F-10: cross-silo grain calls work in a multi-silo in-process cluster.
/// </summary>
public sealed class MultiSiloClusteringTests
{
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddQuarkRuntime();
        services.AddGrain<CounterGrain>();
        services.AddGrainActivatorFactory<CounterGrainActivatorFactory>();
        services.AddLocalClusterClient();
        services.AddGrainProxy<ICounterGrain, CounterGrainProxy>();
    }

    [Fact]
    public async Task Grain_Activated_On_SiloA_Is_Reachable_From_SiloB()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 2;
            options.EnableClustering = true;
            options.ConfigureSiloServices = ConfigureServices;
        });

        // Activate grain via silo 0 (primary silo)
        ICounterGrain grain0 = cluster.Client.GetGrain<ICounterGrain>("shared-grain");
        long val = await grain0.IncrementAsync();
        Assert.Equal(1, val);

        // Access the same grain from silo 1 — should route cross-silo
        var client1 = new TestClient(cluster.Silos[1].Services);
        ICounterGrain grain1 = client1.GetGrain<ICounterGrain>("shared-grain");
        long val1 = await grain1.GetValueAsync();
        Assert.Equal(1, val1);
    }

    [Fact]
    public async Task After_SiloA_Stops_Grain_Can_ReActivate_On_SiloB()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 2;
            options.EnableClustering = true;
            options.ConfigureSiloServices = ConfigureServices;
        });

        // Activate grain on silo 0
        ICounterGrain grain = cluster.Client.GetGrain<ICounterGrain>("restart-grain");
        await grain.IncrementAsync();

        // Stop silo 0 — removes it from the router
        await cluster.Silos[0].StopAsync();

        // Access via silo 1 — directory entry for silo 0 is now stale;
        // TryRouteRemote removes it and grain re-activates locally on silo 1
        var client1 = new TestClient(cluster.Silos[1].Services);
        ICounterGrain grain1 = client1.GetGrain<ICounterGrain>("restart-grain");
        long val = await grain1.GetValueAsync();
        // State is lost (in-memory grain restarted); expect fresh state
        Assert.Equal(0, val);
    }
}
