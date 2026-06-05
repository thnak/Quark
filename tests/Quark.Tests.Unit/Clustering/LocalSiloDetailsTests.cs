using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime; // for AddQuarkRuntime, SiloRuntimeOptions
using Xunit;

namespace Quark.Tests.Unit.Clustering;

/// <summary>Verifies F-12: ILocalSiloDetails resolves from DI with correct values.</summary>
public sealed class LocalSiloDetailsTests
{
    [Fact]
    public void ILocalSiloDetails_Resolves_With_DefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkRuntime();

        using var provider = services.BuildServiceProvider();

        var details = provider.GetRequiredService<ILocalSiloDetails>();
        Assert.NotNull(details);
        Assert.Equal(SiloAddress.Loopback(11111), details.SiloAddress);
        Assert.Equal("QuarkCluster", details.ClusterId);
        Assert.Equal("QuarkService", details.ServiceId);
    }

    [Fact]
    public void ILocalSiloDetails_Reflects_Configured_Options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkRuntime();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.SiloName = "my-silo";
            o.ClusterId = "my-cluster";
            o.ServiceId = "my-service";
            o.SiloAddress = SiloAddress.Loopback(22222);
        });

        using var provider = services.BuildServiceProvider();

        var details = provider.GetRequiredService<ILocalSiloDetails>();
        Assert.Equal("my-silo", details.Name);
        Assert.Equal("my-cluster", details.ClusterId);
        Assert.Equal("my-service", details.ServiceId);
        Assert.Equal(SiloAddress.Loopback(22222), details.SiloAddress);
    }
}
