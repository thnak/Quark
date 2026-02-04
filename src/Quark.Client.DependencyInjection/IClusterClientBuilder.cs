using Microsoft.Extensions.DependencyInjection;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Builder for configuring Quark Cluster Client services.
/// </summary>
public interface IClusterClientBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the client options.
    /// </summary>
    ClusterClientOptions Options { get; }
}