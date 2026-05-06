using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions;
using Quark.Runtime;
using Quark.Testing;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

public sealed class PlacementIntegrationTests
{
    [Fact]
    public void PreferLocalPlacement_Selects_Local_Silo_When_Available()
    {
        ServiceCollection services = new();
        services.AddQuarkRuntime();

        using ServiceProvider provider = services.BuildServiceProvider();
        IPlacementDirector director = provider.GetRequiredService<IPlacementDirector>();

        SiloAddress local = SiloAddress.Loopback(11111);
        SiloAddress remote = SiloAddress.Loopback(11112);
        GrainId grainId = new(new GrainType(nameof(PreferLocalCounterGrain)), "counter-1");

        SiloAddress selected = director.SelectActivationSilo(
            grainId,
            typeof(PreferLocalCounterGrain),
            local,
            [local, remote]);

        Assert.Equal(local, selected);
    }

    [Fact]
    public void HashBasedPlacement_Is_Stable_For_Same_Grain_Key()
    {
        ServiceCollection services = new();
        services.AddQuarkRuntime();

        using ServiceProvider provider = services.BuildServiceProvider();
        IPlacementDirector director = provider.GetRequiredService<IPlacementDirector>();

        SiloAddress local = SiloAddress.Loopback(11111);
        SiloAddress remote1 = SiloAddress.Loopback(11112);
        SiloAddress remote2 = SiloAddress.Loopback(11113);
        GrainId grainId = new(new GrainType(nameof(HashPlacedCounterGrain)), "shopping-cart-42");

        SiloAddress first = director.SelectActivationSilo(grainId, typeof(HashPlacedCounterGrain), local, [local, remote1, remote2]);
        SiloAddress second = director.SelectActivationSilo(grainId, typeof(HashPlacedCounterGrain), local, [remote2, local, remote1]);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TestCluster_Provides_Silos_Compatible_With_Placement_Selection()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(options => options.InitialSilosCount = 2);

        ServiceCollection services = new();
        services.AddQuarkRuntime();

        using ServiceProvider provider = services.BuildServiceProvider();
        IPlacementDirector director = provider.GetRequiredService<IPlacementDirector>();

        SiloAddress local = SiloAddress.Loopback(cluster.Silos[0].SiloPort);
        SiloAddress remote = SiloAddress.Loopback(cluster.Silos[1].SiloPort);
        GrainId grainId = new(new GrainType(nameof(RandomPlacedCounterGrain)), "counter-2");

        SiloAddress selected = director.SelectActivationSilo(
            grainId,
            typeof(RandomPlacedCounterGrain),
            local,
            [local, remote]);

        Assert.Contains(selected, new[] { local, remote });
    }

    [PreferLocalPlacement]
    private sealed class PreferLocalCounterGrain : Grain;

    [HashBasedPlacement]
    private sealed class HashPlacedCounterGrain : Grain;

    [RandomPlacement]
    private sealed class RandomPlacedCounterGrain : Grain;
}
