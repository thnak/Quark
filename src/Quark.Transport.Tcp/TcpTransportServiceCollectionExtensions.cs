using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>Extension methods for registering the TCP transport.</summary>
public static class TcpTransportServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the Quark TCP transport as the default <see cref="ITransport" />.
    /// </summary>
    public static IServiceCollection AddTcpTransport(
        this IServiceCollection services,
        Action<TcpTransportOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<TcpTransport>();
        services.TryAddSingleton<ITransport>(sp => sp.GetRequiredService<TcpTransport>());
        return services;
    }
}
