using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Client;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Extension methods for configuring Quark Cluster Client services.
/// </summary>
public static class ClusterClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds and starts a lightweight Quark Cluster Client to the service collection.
    /// The client connects to a Quark cluster without hosting actors locally.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure client options.</param>
    /// <param name="clientBuilderConfigure">Optional action to configure the client builder (clustering, transport).</param>
    public static void UseQuarkClient(this IServiceCollection services,
        Action<ClusterClientOptions>? configure = null,
        Action<IClusterClientBuilder>? clientBuilderConfigure = null)
    {
        var client = services.AddQuarkClient(configure);
        clientBuilderConfigure?.Invoke(client);
        services.AddHostedService<StartClusterClientHostedService>();
    }

    /// <summary>
    /// Adds a lightweight Quark Cluster Client to the service collection.
    /// The client can connect to a Quark cluster without hosting actors locally.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure client options.</param>
    /// <returns>A builder for further configuration.</returns>
    public static IClusterClientBuilder AddQuarkClient(
        this IServiceCollection services,
        Action<ClusterClientOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure options
        var options = new ClusterClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Register client
        services.TryAddSingleton<IClusterClient, ClusterClient>();

        return new ClusterClientBuilder(services, options);
    }
}

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

internal sealed class ClusterClientBuilder : IClusterClientBuilder
{
    public ClusterClientBuilder(IServiceCollection services, ClusterClientOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public ClusterClientOptions Options { get; }
}
