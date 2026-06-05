using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Quark.Transport.Tcp;

/// <summary>How the local silo validates the remote silo's TLS certificate.</summary>
public enum RemoteCertificateMode
{
    /// <summary>No certificate is required from the remote side.</summary>
    NoCertificate,

    /// <summary>A certificate is accepted regardless of validity (useful for self-signed test certs).</summary>
    AllowAny,

    /// <summary>A valid, trusted certificate is required from the remote side.</summary>
    RequireCertificate,
}

/// <summary>
///     TLS configuration for the Quark TCP transport.
///     Passed to <c>UseTls()</c> on <c>ISiloBuilder</c>.
/// </summary>
public sealed class TlsOptions
{
    /// <summary>
    ///     The certificate presented to the remote silo during TLS handshake.
    ///     Required when acting as server. On the client side, required only when
    ///     <see cref="RemoteCertificateMode.RequireCertificate" /> is set on the server.
    /// </summary>
    public X509Certificate2? LocalCertificate { get; set; }

    /// <summary>How the local silo validates the remote silo's certificate.</summary>
    public RemoteCertificateMode RemoteCertificateMode { get; set; } = RemoteCertificateMode.RequireCertificate;

    /// <summary>
    ///     Optional callback invoked on the client side during TLS handshake.
    ///     Can be used to customise <see cref="SslClientAuthenticationOptions" />.
    /// </summary>
    public Action<SslClientAuthenticationOptions>? OnAuthenticateAsClient { get; set; }

    /// <summary>
    ///     Optional callback invoked on the server side during TLS handshake.
    ///     Can be used to customise <see cref="SslServerAuthenticationOptions" />.
    /// </summary>
    public Action<SslServerAuthenticationOptions>? OnAuthenticateAsServer { get; set; }

    /// <summary>Sets <see cref="RemoteCertificateMode" /> to <see cref="RemoteCertificateMode.AllowAny" />.</summary>
    public void AllowAnyRemoteCertificate()
        => RemoteCertificateMode = RemoteCertificateMode.AllowAny;
}
