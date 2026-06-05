using Microsoft.Extensions.DependencyInjection;

namespace Quark.Testing.Harness;

/// <summary>
///     Options for configuring a <see cref="TestCluster" />.
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

    /// <summary>
    ///     When <see langword="true" />, all silos in the cluster share a grain directory, router,
    ///     and membership table so grains are reachable across silo boundaries.
    ///     Default: <see langword="false" /> (each silo is independent).
    /// </summary>
    public bool EnableClustering { get; set; }

    /// <summary>
    ///     Shared clustering state injected into each silo when <see cref="EnableClustering" /> is true.
    ///     Created automatically by <see cref="TestCluster" />.
    /// </summary>
    internal SharedTestClusterState? SharedClusterState { get; set; }
}
