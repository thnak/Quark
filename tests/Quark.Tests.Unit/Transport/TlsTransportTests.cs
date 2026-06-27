using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Unit.Transport;

/// <summary>Verifies F-13: TLS transport wraps TCP connections with SslStream.</summary>
public sealed class TlsTransportTests
{
    private static X509Certificate2 CreateSelfSignedCert(string subject = "CN=QuarkTest")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 cert = req.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(1));
        // Export to PFX and reload so the private key lands in a key set usable by SChannel
        // server-side TLS on Windows. The previous PEM round-trip produced an ephemeral key
        // that AuthenticateAsServerAsync rejects on Windows, causing the server to close the
        // socket mid-handshake and the client to see "unexpected EOF" (issue #48).
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    [Fact]
    public async Task TlsConnection_Establishes_With_SelfSigned_Cert_And_AllowAny()
    {
        X509Certificate2 serverCert = CreateSelfSignedCert();
        var serverOptions = new TcpTransportOptions
        {
            Tls = new TlsOptions
            {
                LocalCertificate = serverCert,
                RemoteCertificateMode = RemoteCertificateMode.NoCertificate,
            },
        };
        var clientOptions = new TcpTransportOptions
        {
            Tls = new TlsOptions { RemoteCertificateMode = RemoteCertificateMode.AllowAny },
        };

        var serverTransport = new TcpTransport(serverOptions, NullLogger<TcpTransport>.Instance);
        var clientTransport = new TcpTransport(clientOptions, NullLogger<TcpTransport>.Instance);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ITransportListener listener = serverTransport.CreateListener(endPoint);
        await listener.BindAsync();
        var actualEndPoint = listener.LocalEndPoint;

        var acceptTask = listener.AcceptAsync();
        await using ITransportConnection client = await clientTransport.ConnectAsync(actualEndPoint);
        ITransportConnection? server = await acceptTask;

        // Both sides connected without exception — TLS handshake succeeded
        Assert.NotNull(server);
        await server.DisposeAsync();
        await listener.StopAsync();
        await listener.DisposeAsync();
    }

    [Fact]
    public async Task TlsConnection_Fails_When_Server_Has_No_Certificate()
    {
        // Server has no certificate configured; TLS authentication will fail
        var serverOptions = new TcpTransportOptions
        {
            Tls = new TlsOptions { RemoteCertificateMode = RemoteCertificateMode.NoCertificate },
        };
        var clientOptions = new TcpTransportOptions
        {
            Tls = new TlsOptions { RemoteCertificateMode = RemoteCertificateMode.AllowAny },
        };

        var serverTransport = new TcpTransport(serverOptions, NullLogger<TcpTransport>.Instance);
        var clientTransport = new TcpTransport(clientOptions, NullLogger<TcpTransport>.Instance);

        ITransportListener listener = serverTransport.CreateListener(new IPEndPoint(IPAddress.Loopback, 0));
        await listener.BindAsync();

        _ = listener.AcceptAsync(); // Start accepting; it will fail the TLS handshake silently
        await Assert.ThrowsAnyAsync<Exception>(
            () => clientTransport.ConnectAsync(listener.LocalEndPoint));

        await listener.StopAsync();
        await listener.DisposeAsync();
    }
}
