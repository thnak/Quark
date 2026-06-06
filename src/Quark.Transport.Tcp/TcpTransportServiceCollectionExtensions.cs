using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        // Bridge IOptions<TcpTransportOptions> → TcpTransportOptions so that
        // TcpTransport can take the concrete class (no IOptions<T> dependency in the ctor).
        services.TryAddSingleton<TcpTransportOptions>(
            sp => sp.GetRequiredService<IOptions<TcpTransportOptions>>().Value);

        services.TryAddSingleton<TcpTransport>();
        services.TryAddSingleton<ITransport>(sp => sp.GetRequiredService<TcpTransport>());
        return services;
    }
}
