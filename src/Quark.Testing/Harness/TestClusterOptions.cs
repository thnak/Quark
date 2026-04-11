using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quark.Testing.Harness;

/// <summary>
/// Options for configuring a <see cref="TestCluster"/>.
/// </summary>
public sealed class TestClusterOptions
{
    /// <summary>Number of silos to start. Default: 2.</summary>
    public int InitialSilosCount { get; set; } = 2;

    /// <summary>Starting port for silo-to-silo communication. Default: 30000.</summary>
    public int BaseSiloPort { get; set; } = 30_000;

    /// <summary>Starting port for client gateway. Default: 40000.</summary>
    public int BaseGatewayPort { get; set; } = 40_000;

    /// <summary>Called to add additional services to every silo's DI container.</summary>
    public Action<IServiceCollection>? ConfigureSiloServices { get; set; }

    /// <summary>Called to add additional services to the client's DI container.</summary>
    public Action<IServiceCollection>? ConfigureClientServices { get; set; }
}
