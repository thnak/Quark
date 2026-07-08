using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Hosting;

namespace Quark.Transport.Tcp;

/// <summary>
///     Silo builder extension methods for configuring the TCP transport.
/// </summary>
public static class TcpTransportSiloBuilderExtensions
{
    /// <summary>
    ///     Configures TLS for all silo-to-silo TCP connections.
    ///     Drop-in equivalent of Orleans' <c>UseTls()</c>.
    /// </summary>
    public static ISiloBuilder UseTls(this ISiloBuilder builder, Action<TlsOptions> configure)
    {
        builder.Configure<TcpTransportOptions>(o =>
        {
            o.Tls ??= new TlsOptions();
            configure(o.Tls);
        });
        return builder;
    }
}
