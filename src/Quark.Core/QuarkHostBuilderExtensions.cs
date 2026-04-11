using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Core.Hosting;
using Quark.Serialization;

namespace Quark.Core;

/// <summary>
/// Extension methods for adding Quark to an <see cref="IHostBuilder"/>.
/// </summary>
public static class QuarkHostBuilderExtensions
{
    /// <summary>
    /// Configures the host to run a Quark silo.
    /// </summary>
    public static IHostBuilder UseQuarkSilo(
        this IHostBuilder builder,
        Action<ISiloBuilder>? configureSilo = null)
    {
        return builder.ConfigureServices((_, services) =>
        {
            services.AddQuarkSilo(configureSilo);
        });
    }

    /// <summary>
    /// Configures the host to run a Quark cluster client.
    /// </summary>
    public static IHostBuilder UseQuarkClient(
        this IHostBuilder builder,
        Action<IClientBuilder>? configureClient = null)
    {
        return builder.ConfigureServices((_, services) =>
        {
            services.AddQuarkClient(configureClient);
        });
    }
}
