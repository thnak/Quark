using Microsoft.Extensions.Hosting;
using Quark.Core.Hosting;

namespace Quark.Core;

/// <summary>
///     Extension methods for adding Quark to an <see cref="IHostBuilder" /> or <see cref="IHostApplicationBuilder" />.
/// </summary>
public static class QuarkHostBuilderExtensions
{
    /// <summary>
    ///     Configures the host to run a Quark silo.
    ///     Drop-in equivalent of Orleans' <c>UseOrleans()</c>.
    /// </summary>
    /// <remarks>Works with the legacy <see cref="IHostBuilder" /> pattern.</remarks>
    public static IHostBuilder UseQuark(
        this IHostBuilder builder,
        Action<ISiloBuilder>? configureSilo = null)
    {
        return builder.ConfigureServices((_, services) => { services.AddQuarkSilo(configureSilo); });
    }

    /// <summary>
    ///     Configures the host to run a Quark silo.
    ///     Drop-in equivalent of Orleans' <c>UseOrleans()</c>.
    ///     Works with the modern <see cref="IHostApplicationBuilder" /> pattern (e.g. <c>Host.CreateApplicationBuilder()</c>).
    /// </summary>
    public static IHostApplicationBuilder UseQuark(
        this IHostApplicationBuilder builder,
        Action<ISiloBuilder>? configureSilo = null)
    {
        builder.Services.AddQuarkSilo(configureSilo);
        return builder;
    }

    /// <summary>
    ///     Configures the host to run a Quark cluster client.
    /// </summary>
    /// <remarks>Works with the legacy <see cref="IHostBuilder" /> pattern.</remarks>
    public static IHostBuilder UseQuarkClient(
        this IHostBuilder builder,
        Action<IClientBuilder>? configureClient = null)
    {
        return builder.ConfigureServices((_, services) => { services.AddQuarkClient(configureClient); });
    }

    // Kept for backwards compatibility.
    /// <summary>
    ///     Configures the host to run a Quark silo (legacy name — prefer <see cref="UseQuark" />).
    /// </summary>
    public static IHostBuilder UseQuarkSilo(
        this IHostBuilder builder,
        Action<ISiloBuilder>? configureSilo = null)
    {
        return builder.UseQuark(configureSilo);
    }
}
