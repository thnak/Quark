using Microsoft.Extensions.DependencyInjection;

namespace Quark.Core.Hosting;

/// <summary>
/// Builder interface for configuring a Quark silo.
/// </summary>
public interface ISiloBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures silo options.
    /// </summary>
    ISiloBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new();
}

/// <summary>
/// Extension methods for <see cref="ISiloBuilder"/>.
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// Configures the silo for single-node local development (localhost clustering).
    /// Drop-in equivalent of Orleans' <c>UseLocalhostClustering()</c>.
    /// </summary>
    public static ISiloBuilder UseLocalhostClustering(
        this ISiloBuilder builder,
        int siloPort = 11111,
        int gatewayPort = 30000,
        string clusterId = "dev",
        string serviceId = "QuarkService")
    {
        // No external membership store needed for single-node; all defaults are loopback.
        // This is a no-op for now because InMemoryGrainDirectory + localhost are already defaults.
        // Will wire real membership provider in M3/M4.
        return builder;
    }
}

