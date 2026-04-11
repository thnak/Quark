using Microsoft.Extensions.DependencyInjection;
using Quark.Testing;
using Xunit;

namespace Quark.Tests.Unit.Testing;

public sealed class TestClusterHarnessTests
{
    [Fact]
    public async Task CreateAsync_Starts_Default_Silos_And_Client()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync();

        Assert.Equal(2, cluster.Silos.Count);
        Assert.All(cluster.Silos, silo => Assert.True(silo.IsStarted));
        Assert.True(cluster.Client.IsInitialized);
    }

    [Fact]
    public async Task CreateAsync_Applies_Silo_And_Client_Service_Configuration()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services => services.AddSingleton<SiloMarker>();
            options.ConfigureClientServices = services => services.AddSingleton<ClientMarker>();
        });

        Assert.NotNull(cluster.PrimarySilo.GetRequiredService<SiloMarker>());
        Assert.NotNull(cluster.Client.GetRequiredService<ClientMarker>());
    }

    [Fact]
    public async Task StopAsync_Clears_Silos_And_Client_State()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync();

        await cluster.StopAsync();

        Assert.Empty(cluster.Silos);
        Assert.Throws<InvalidOperationException>(() => _ = cluster.Client);
    }

    private sealed class SiloMarker;
    private sealed class ClientMarker;
}
